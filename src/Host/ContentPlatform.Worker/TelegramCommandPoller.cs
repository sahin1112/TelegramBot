using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ContentPlatform.Abstractions;
using ContentPlatform.Editorial.Application;
using ContentPlatform.Editorial.Domain;
using ContentPlatform.Platform.Application;
using ContentPlatform.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using PlatformKind = ContentPlatform.Abstractions.Platform;

namespace ContentPlatform.Worker;

/// <summary>
/// Telegram long-polling dinleyicisi:
///  * "/getid" (veya "/id", "/chatid") → sohbetin chat_id'si (her sohbette çalışır).
///  * "/&lt;kategori-slug&gt; &lt;link&gt;" (ör. /kripto https://...) → link o KATEGORİDE AI ile üretilir
///    ve ONAY kuyruğuna düşer (otomatik yayınlanmaz). YALNIZ admin grubundan (telegram.admin_chat_id).
///  * "/onayla" → onay kuyruğundaki TÜM içerikleri onaylar. YALNIZ admin grubundan.
///
/// YALNIZ Worker'da calisir (tek instance). Api pollememeli: ayni token'da iki getUpdates → 409 conflict.
/// Gonderim akisini ETKILEMEZ (ayri istek). Token(lar) sunucuda sifreli saklanan SocialAccount'tan cozulur.
/// </summary>
public sealed class TelegramCommandPoller(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<TelegramCommandPoller> logger) : BackgroundService
{
    private readonly Dictionary<string, long> _offsets = new(); // token -> bir sonraki offset
    private readonly Dictionary<string, string> _cmdSignatures = new(); // token -> son kaydedilen komut imzası
    private readonly Dictionary<Guid, DateTimeOffset> _invalidTokenWarnedAt = new(); // hesap -> son geçersiz-token uyarısı
    private DateTimeOffset _lastCmdSync = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelegramCommandPoller basladi — '/getid' komutu dinleniyor.");
        // Yeniden baslatinca eski (bekleyen) komutlara yanit vermemek icin ilk turda sadece 'drain' et.
        var firstPass = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tokens = await GetBotTokensAsync(stoppingToken);
                if (tokens.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                // Telegram "/" komut MENÜSÜ: aktif kategoriler + /onayla + /getid otomatik kaydedilir.
                // Yeni kategori panelden eklenince (≤5 dk içinde) menüde kendiliğinden belirir.
                await SyncBotCommandsAsync(tokens, stoppingToken);

                foreach (var token in tokens)
                    await PollOnceAsync(token, drainOnly: firstPass, stoppingToken);

                firstPass = false;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TelegramCommandPoller dongu hatasi");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetBotTokensAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISocialAccountRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<SocialAccountService>();
        var accounts = await repo.ListByPlatformAsync(PlatformKind.Telegram, ct);

        var tokens = new List<string>();
        foreach (var acc in accounts)
        {
            try
            {
                var creds = svc.DecryptCredentials(acc);
                if (creds.TryGetValue("BotToken", out var t) && !string.IsNullOrWhiteSpace(t))
                {
                    t = t.Trim();
                    // Biçimi bozuk token'ı (ör. token alanına şifre yazılmışsa) POLLAMA — aksi halde her
                    // 25 sn'de bir 404 + "getUpdates hatasi" spam'i olur. Uyarı hesap adıyla, saatte 1 kez.
                    if (!ContentPlatform.Abstractions.TelegramToken.LooksValid(t))
                    {
                        if (_invalidTokenWarnedAt.TryGetValue(acc.Id, out var last) is false || DateTimeOffset.UtcNow - last > TimeSpan.FromHours(1))
                        {
                            _invalidTokenWarnedAt[acc.Id] = DateTimeOffset.UtcNow;
                            logger.LogWarning("Telegram hesabında GEÇERSİZ BotToken: hesap=\"{Name}\" ({Acc}) değer='{Masked}' — Panel → Sosyal Hesaplar → Düzenle ile gerçek BotFather token'ını girin. Bu hesap atlanıyor.",
                                acc.DisplayName, acc.Id, ContentPlatform.Abstractions.TelegramToken.Mask(t));
                        }
                        continue;
                    }
                    tokens.Add(t);
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Bot token cozulemedi: hesap={Acc}", acc.Id); }
        }
        return tokens.Distinct().ToList();
    }

    private async Task PollOnceAsync(string token, bool drainOnly, CancellationToken ct)
    {
        // Varsayilan HttpClient (100 sn timeout, direncsiz) — 25 sn long-poll icin uygun.
        var client = httpClientFactory.CreateClient();
        var baseUrl = $"https://api.telegram.org/bot{token}";
        _offsets.TryGetValue(token, out var offset);

        // allowed_updates = ["message","channel_post","callback_query"]
        var url = $"{baseUrl}/getUpdates?timeout=25&allowed_updates=%5B%22message%22%2C%22channel_post%22%2C%22callback_query%22%5D";
        if (offset > 0) url += $"&offset={offset}";

        TgUpdatesResponse? resp;
        try
        {
            resp = await client.GetFromJsonAsync<TgUpdatesResponse>(url, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "getUpdates hatasi (webhook acik olabilir ya da ag sorunu)");
            return;
        }

        if (resp is null || !resp.Ok || resp.Result is null || resp.Result.Count == 0)
            return;

        var maxId = offset - 1;
        foreach (var u in resp.Result)
        {
            if (u.UpdateId > maxId) maxId = u.UpdateId;
            if (drainOnly) continue;

            if (u.CallbackQuery is { } cq)
            {
                try { await HandleCallbackAsync(baseUrl, client, cq, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "Telegram buton islenemedi: {Data}", cq.Data); }
                continue;
            }
            var msg = u.Message ?? u.ChannelPost;
            if (msg?.Chat is null) continue;
            if (IsGetIdCommand(msg.Text))
            { await ReplyIdAsync(baseUrl, client, msg.Chat, ct); continue; }
            if (msg.Text is { } cmdText && cmdText.TrimStart().StartsWith('/'))
            {
                // Komut işleme tek tek ve korumalı: bir komutun hatası polleri durdurmaz.
                try { await HandleAdminCommandAsync(baseUrl, client, msg, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "Telegram komutu islenemedi: {Text}", msg.Text); }
            }
        }
        _offsets[token] = maxId + 1;
    }

    private static bool IsGetIdCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var first = text.Trim().Split(new[] { ' ', '\n', '\t' }, 2)[0];
        var at = first.IndexOf('@'); if (at >= 0) first = first[..at]; // "/getid@Bot" → "/getid"
        return first.Equals("/getid", StringComparison.OrdinalIgnoreCase)
            || first.Equals("/id", StringComparison.OrdinalIgnoreCase)
            || first.Equals("/chatid", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>/onayla ve /&lt;kategori&gt; &lt;link&gt; komutları. Yalnız telegram.admin_chat_id sohbetinden kabul edilir.</summary>
    private async Task HandleAdminCommandAsync(string baseUrl, HttpClient client, TgMessage msg, CancellationToken ct)
    {
        var text = msg.Text!.Trim();
        var head = text.Split(new[] { ' ', '\n', '\t' }, 2)[0];
        var at = head.IndexOf('@'); if (at >= 0) head = head[..at]; // "/kripto@Bot" → "/kripto"
        var cmd = Norm(head.TrimStart('/'));
        if (cmd.Length == 0) return;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // Komut, aktif bir kategori slug/adına ya da "onayla"ya eşleşmiyorsa SESSİZCE geç
        // (başka botların komutlarına, alakasız /yazışmalara karışma).
        var cats = await sp.GetRequiredService<IPublicCategoryProvider>().GetActiveAsync(ct);
        var cat = cats.FirstOrDefault(c => Norm(c.Slug) == cmd || Norm(c.Name) == cmd);
        var isOnayla = cmd is "onayla" or "approve";
        if (cat is null && !isOnayla) return;

        // GÜVENLİK: içerik üreten/onaylayan komutlar yalnız TANIMLI admin grubundan çalışır.
        var settings = sp.GetRequiredService<ISettingsProvider>();
        var adminChat = (await settings.GetAsync("telegram.admin_chat_id", ct))?.Trim();
        if (string.IsNullOrWhiteSpace(adminChat))
        {
            await SendAsync(baseUrl, client, msg.Chat!.Id,
                "⚠️ Admin grubu tanımlı değil. Panel → Ayarlar'a <code>telegram.admin_chat_id</code> anahtarıyla bu sohbetin ID'sini girin " +
                $"(bu sohbet: <code>{msg.Chat.Id}</code>).", ct);
            return;
        }
        if (!SameChatId(adminChat, msg.Chat!.Id))
        {
            // Teşhis için İKİ değer de loglanır — panele yapıştırırken görünmez karakter/yanlış ID
            // karışırsa buradan yakalanır. (Sohbete cevap YAZILMAZ: yabancı gruba bilgi sızmasın.)
            logger.LogWarning("Yetkisiz sohbetten komut yok sayıldı: gelen={Chat} beklenen='{Expected}' cmd=/{Cmd}", msg.Chat.Id, adminChat, cmd);
            return;
        }

        if (isOnayla) { await ApproveAllPendingAsync(sp, baseUrl, client, msg.Chat.Id, ct); return; }

        // ---- /<kategori> <link> ----
        var url = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(p => p.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                              || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        if (url is null)
        {
            await SendAsync(baseUrl, client, msg.Chat.Id,
                $"Kullanım: <code>/{Html(cmd)} https://haber-linki</code>\nLink, <b>{Html(cat!.Name)}</b> kategorisinde AI ile üretilip ONAYA düşer.", ct);
            return;
        }

        await SendAsync(baseUrl, client, msg.Chat.Id, $"⏳ Alındı — <b>{Html(cat!.Name)}</b> kategorisinde üretiliyor… (~1 dk)", ct);
        try
        {
            var manual = sp.GetRequiredService<ManualContentService>();
            var gen = sp.GetRequiredService<ContentGenerationService>();
            var repo = sp.GetRequiredService<IContentRepository>();
            var clock = sp.GetRequiredService<IClock>();

            var id = await manual.AddFromLinkForReviewAsync(url, cat.Id, $"tg:{msg.Chat.Id}", ct);
            var r = await gen.GenerateDraftAsync(id, null, ct); // seed yok → SourceUrl'den TAM makale çekilir
            if (r.IsFailure)
            {
                await SendAsync(baseUrl, client, msg.Chat.Id, "❌ Üretim başarısız: " + Html(r.Error.Message), ct);
                return;
            }
            var item = await repo.GetAsync(id, ct);
            var sub = item!.SubmitForReview(clock);
            await repo.SaveChangesAsync(ct);
            var title = item.Revisions.FirstOrDefault(x => x.IsCurrent)?.Title ?? "(başlık yok)";
            if (sub.IsFailure)
            {
                await SendAsync(baseUrl, client, msg.Chat.Id, "⚠️ Üretildi ama onaya gönderilemedi: " + Html(sub.Error.Message), ct);
                return;
            }
            // BLOG YAZISI da gruba gönderilir — butona basmadan önce NE onayladığını gör.
            // (Telegram 4096 kr sınırı: uzun yazı paragraf sınırından parçalara bölünür.)
            var body = BodyToTelegram(item.Revisions.FirstOrDefault(x => x.IsCurrent)?.BodyHtml);
            if (body.Length > 0)
            {
                var chunks = ChunkText(body).ToList();
                for (var ci = 0; ci < chunks.Count; ci++)
                    await SendAsync(baseUrl, client, msg.Chat.Id,
                        (chunks.Count > 1 ? $"📝 <b>Blog yazısı ({ci + 1}/{chunks.Count})</b>\n\n" : "📝 <b>Blog yazısı</b>\n\n") + chunks[ci], ct);
            }
            // Görsel/video tercihi BUTONLARLA sorulur — bastığın seçenekle YALNIZ BU içerik onaylanıp yayına girer.
            var kb = new
            {
                inline_keyboard = new object[]
                {
                    new object[]
                    {
                        new { text = "🖼 SkiaCard onayla", callback_data = $"ap|{id:N}|skia" },
                        new { text = "🤖 AI görsel onayla", callback_data = $"ap|{id:N}|ai" }
                    },
                    new object[] { new { text = "🎬 AI görsel + video onayla", callback_data = $"ap|{id:N}|aivid" } },
                    new object[] { new { text = "❌ Reddet", callback_data = $"rej|{id:N}" } }
                }
            };
            await SendAsync(baseUrl, client, msg.Chat.Id,
                $"✅ Üretildi, ONAY bekliyor:\n<b>{Html(title)}</b>\n\nNasıl devam edelim? (Panelden de bakabilirsin — <code>/onayla</code> Telegram'dan eklediklerinin hepsini varsayılanla onaylar; öteki bekleyenlere dokunmaz.)",
                ct, kb);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram link komutu başarısız: /{Cmd} {Url}", cmd, url);
            await SendAsync(baseUrl, client, msg.Chat.Id, "❌ Hata: " + Html(ex.Message), ct);
        }
    }

    /// <summary>Üretim mesajındaki butonlar: ap|id|skia · ap|id|ai · ap|id|aivid · rej|id.
    /// Seçime göre YALNIZ o içerik işlenir: (video) → görsel → onay → yayın (kadansa göre planlanır).</summary>
    private async Task HandleCallbackAsync(string baseUrl, HttpClient client, TgCallbackQuery cq, CancellationToken ct)
    {
        // Butona basıldı bilgisini HEMEN ver (Telegram spinner'ı kalksın); iş arkadan sürer.
        try { using var _ = await client.PostAsJsonAsync($"{baseUrl}/answerCallbackQuery", new { callback_query_id = cq.Id }, ct); }
        catch { /* önemsiz */ }

        var chatId = cq.Message?.Chat?.Id;
        var data = cq.Data ?? "";
        var p = data.Split('|');
        if (chatId is null || p.Length < 2 || !Guid.TryParse(p[1], out var id)) return;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // GÜVENLİK: butonlar da yalnız admin grubunda işlenir.
        var adminChat = (await sp.GetRequiredService<ISettingsProvider>().GetAsync("telegram.admin_chat_id", ct))?.Trim();
        if (string.IsNullOrWhiteSpace(adminChat) || !SameChatId(adminChat, chatId.Value))
        { logger.LogWarning("Yetkisiz sohbetten buton yok sayıldı: gelen={Chat} beklenen='{Expected}'", chatId, adminChat); return; }

        var repo = sp.GetRequiredService<IContentRepository>();
        var audit = sp.GetRequiredService<IContentAudit>();
        var gen = sp.GetRequiredService<ContentGenerationService>();
        var clock = sp.GetRequiredService<IClock>();

        var item = await repo.GetAsync(id, ct);
        if (item is null) { await SendAsync(baseUrl, client, chatId.Value, "İçerik bulunamadı (silinmiş olabilir).", ct); return; }
        var title = item.Revisions.FirstOrDefault(x => x.IsCurrent)?.Title ?? "(başlık yok)";

        if (p[0] == "rej")
        {
            var rr = item.Reject("telegram-admin", clock);
            if (rr.IsSuccess) { audit.Log(id, AuditEvent.Rejected, ActorType.TelegramMember, "telegram-admin"); await repo.SaveChangesAsync(ct); }
            await SendAsync(baseUrl, client, chatId.Value,
                rr.IsSuccess ? $"❌ Reddedildi: <b>{Html(title)}</b>" : "Reddedilemedi: " + Html(rr.Error.Message), ct);
            return;
        }
        if (p[0] != "ap" || p.Length < 3) return;
        if (item.EditorialStatus is not (EditorialStatus.PendingReview or EditorialStatus.Draft))
        { await SendAsync(baseUrl, client, chatId.Value, $"Bu içerik zaten işlenmiş (durum: {item.EditorialStatus}).", ct); return; }

        var choice = p[2]; // skia | ai | aivid
        var source = choice == "skia" ? ImageSource.SkiaCard : ImageSource.Ai;

        // SIRALAMA ÖNEMLİ: içerik onaydan ÖNCE medyası üretilir (PendingReview'dayken Worker döngüsü
        // dokunmaz → yarış/çifte üretim olmaz). Sonra onay + yayın; kadans varsa plana alınır.
        if (choice == "aivid")
        {
            await SendAsync(baseUrl, client, chatId.Value, $"🎬 Video üretiliyor… (~30 sn)\n<b>{Html(title)}</b>", ct);
            var rv = await gen.GeneratePreviewVideoAsync(id, null, aiBackground: true, ct);
            if (rv.IsFailure)
                await SendAsync(baseUrl, client, chatId.Value, "⚠️ Video üretilemedi (" + Html(rv.Error.Message) + ") — görselle devam ediliyor.", ct);
        }

        await SendAsync(baseUrl, client, chatId.Value, choice == "skia" ? "🖼 Görsel üretiliyor…" : "🤖 AI görsel üretiliyor…", ct);
        var ri = await gen.GeneratePreviewImageAsync(id, source, null, ct);
        if (ri.IsFailure && source == ImageSource.Ai)
        {
            // AI görsel patlarsa içerik TAKILMASIN: bilgilendir + SkiaCard'a düş.
            await SendAsync(baseUrl, client, chatId.Value, "⚠️ AI görsel üretilemedi (" + Html(ri.Error.Message) + ") — SkiaCard ile devam ediliyor.", ct);
            ri = await gen.GeneratePreviewImageAsync(id, ImageSource.SkiaCard, null, ct);
        }
        if (ri.IsFailure)
        { await SendAsync(baseUrl, client, chatId.Value, "❌ Görsel üretilemedi: " + Html(ri.Error.Message), ct); return; }

        var ar = item.Approve("telegram-admin", automated: false, clock);
        if (ar.IsFailure)
        { await SendAsync(baseUrl, client, chatId.Value, "❌ Onaylanamadı: " + Html(ar.Error.Message), ct); return; }
        audit.Log(id, AuditEvent.Approved, ActorType.TelegramMember, "telegram-admin", $"Telegram buton ({choice})");
        await repo.SaveChangesAsync(ct);

        var pr = await gen.PublishExistingAsync(id, adGate: false, ct);
        await SendAsync(baseUrl, client, chatId.Value, pr.IsSuccess
            ? $"✅ Onaylandı ve yayına gönderildi:\n<b>{Html(title)}</b>\nKadans tanımlıysa plana alındı, değilse hemen gidiyor." + (choice == "aivid" ? " (video dahil 🎬)" : "")
            : "⚠️ Onaylandı ama yayın tetiklenemedi: " + Html(pr.Error.Message), ct);
    }

    /// <summary>YALNIZ Telegram komutlarıyla eklenen (Origin=TelegramAdmin) bekleyen içerikleri onaylar —
    /// RSS/panel kaynaklı diğer bekleyenlere DOKUNMAZ, onlar kuyrukta kalır. Metin+görseli hazır olanlar
    /// hemen yayına gönderilir; kalanını PipelineDrainJob ~1 dk içinde üretip yayınlar.</summary>
    private async Task ApproveAllPendingAsync(IServiceProvider sp, string baseUrl, HttpClient client, long chatId, CancellationToken ct)
    {
        var repo = sp.GetRequiredService<IContentRepository>();
        var audit = sp.GetRequiredService<IContentAudit>();
        var gen = sp.GetRequiredService<ContentGenerationService>();
        var clock = sp.GetRequiredService<IClock>();

        var items = (await repo.GetByStatusAsync(EditorialStatus.PendingReview, 100, ct))
            .Where(x => x.Origin == ContentOrigin.TelegramAdmin).ToList();
        if (items.Count == 0)
        {
            await SendAsync(baseUrl, client, chatId, "Telegram'dan eklenen bekleyen içerik yok 👍 (panel/RSS kuyruğuna dokunmuyorum.)", ct);
            return;
        }
        var ok = 0; var publishedNow = 0;
        foreach (var it in items)
        {
            if (!it.Approve("telegram-admin", automated: false, clock).IsSuccess) continue;
            audit.Log(it.Id, AuditEvent.Approved, ActorType.TelegramMember, "telegram-admin", "Telegram /onayla (toplu)");
            ok++;
            await repo.SaveChangesAsync(ct);
            // Metin+görseli zaten hazırsa bekletmeden yayına gönder (panel toplu onayla aynı kural).
            if (it.MediaStatus == MediaStatus.Ready && (await gen.PublishExistingAsync(it.Id, adGate: false, ct)).IsSuccess)
                publishedNow++;
        }
        await repo.SaveChangesAsync(ct);
        await SendAsync(baseUrl, client, chatId,
            $"✅ Telegram'dan eklenen {ok}/{items.Count} içerik onaylandı" + (publishedNow > 0 ? $", {publishedNow} tanesi hemen yayına gönderildi" : "") +
            " — kalanını Worker ~1 dk içinde üretip yayınlar. Öteki bekleyenlere dokunulmadı.", ct);
    }

    /// <summary>Ayardan gelen sohbet ID'sini TOLERANSLI karşılaştırır: yalnız rakam ve eksi işareti
    /// dikkate alınır — panele kopyala/yapıştırda karışan boşluk, tırnak, görünmez karakter (zero-width
    /// vb.) ve farklı tire türleri eşleşmeyi bozamaz.</summary>
    private static bool SameChatId(string? setting, long chatId)
    {
        var cleaned = Regex.Replace((setting ?? "").Replace('−', '-'), @"[^\d-]", ""); // önce U+2212 eksi → '-', sonra temizlik
        return cleaned.Length > 0 && cleaned == chatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Norm(string s)
    {
        s = s.Trim().ToLowerInvariant().Replace("\u0307", "");
        // '-' → '_': Telegram komutlarında tire geçersizdir; "yapay-zeka" slug'ı menüye /yapay_zeka
        // olarak kaydedilir — ikisi de aynı komuta çözülsün.
        return s.Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c').Replace('â', 'a').Replace('î', 'i').Replace('û', 'u').Replace('-', '_');
    }

    /// <summary>Bot komut menüsünü (setMyCommands) aktif kategorilerle senkron tutar — 5 dk'da bir
    /// kontrol edilir, liste DEĞİŞTİYSE Telegram'a yazılır. Yeni kategori = menüde yeni komut, kod yok.</summary>
    private async Task SyncBotCommandsAsync(IReadOnlyList<string> tokens, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastCmdSync < TimeSpan.FromMinutes(5)) return;
        _lastCmdSync = DateTimeOffset.UtcNow;

        List<object> commands; string sig;
        using (var scope = scopeFactory.CreateScope())
        {
            var cats = await scope.ServiceProvider.GetRequiredService<IPublicCategoryProvider>().GetActiveAsync(ct);
            var list = new List<(string Cmd, string Desc)>
            {
                ("onayla", "Telegram'dan eklenen bekleyenleri onayla"),
                ("getid", "Bu sohbetin ID'sini göster")
            };
            foreach (var c in cats)
            {
                var cmd = TgCommand(c.Slug.Length > 0 ? c.Slug : c.Name);
                if (cmd.Length == 0 || list.Any(x => x.Cmd == cmd)) continue;
                var desc = $"Linkten '{c.Name}' kategorisinde içerik üret";
                list.Add((cmd, desc.Length > 256 ? desc[..256] : desc));
            }
            commands = list.Select(x => (object)new { command = x.Cmd, description = x.Desc }).ToList();
            sig = string.Join('|', list.Select(x => x.Cmd));
        }

        var client = httpClientFactory.CreateClient();
        foreach (var token in tokens)
        {
            if (_cmdSignatures.TryGetValue(token, out var old) && old == sig) continue;
            try
            {
                using var r = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/setMyCommands", new { commands }, ct);
                if (r.IsSuccessStatusCode)
                {
                    _cmdSignatures[token] = sig;
                    logger.LogInformation("Telegram komut menüsü güncellendi ({N} komut).", commands.Count);
                }
                else logger.LogWarning("setMyCommands HTTP {Code}", (int)r.StatusCode);
            }
            catch (Exception ex) { logger.LogWarning(ex, "setMyCommands başarısız"); }
        }
    }

    /// <summary>Slug'ı Telegram komut biçimine indirger: a-z, 0-9, alt çizgi; en çok 32 karakter.</summary>
    private static string TgCommand(string slug)
    {
        var s = Norm(slug);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_') sb.Append(ch);
        var r = sb.ToString();
        return r.Length > 32 ? r[..32] : r;
    }

    private static string Html(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    /// <summary>Blog HTML'ini Telegram'ın dar HTML'ine çevirir: h2/h3 → kalın satır, p/br → satır sonu,
    /// kalan etiketler atılır; metin Telegram için kaçışlanır (yalnız &amp; &lt; &gt;).</summary>
    internal static string BodyToTelegram(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = html!;
        s = Regex.Replace(s, @"<h[2-4][^>]*>(.*?)</h[2-4]>", m => "\n\n\u0001" + m.Groups[1].Value + "\u0002\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</p>\s*", "\n\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<.*?>", "");
        s = System.Net.WebUtility.HtmlDecode(s); // kaynak HTML'deki &amp; vb. çöz
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
        s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); // Telegram kaçışı
        return s.Replace("\u0001", "<b>").Replace("\u0002", "</b>");
    }

    /// <summary>Metni Telegram sınırına (4096) sığacak parçalara böler — kesim paragraf sınırından.</summary>
    internal static IEnumerable<string> ChunkText(string text, int max = 3500)
    {
        while (text.Length > max)
        {
            var cut = text.LastIndexOf("\n\n", max, StringComparison.Ordinal);
            if (cut < max / 2) cut = max;
            yield return text[..cut];
            text = text[cut..].TrimStart();
        }
        if (text.Length > 0) yield return text;
    }

    private async Task SendAsync(string baseUrl, HttpClient client, long chatId, string html, CancellationToken ct, object? replyMarkup = null)
    {
        try
        {
            object payload = replyMarkup is null
                ? new { chat_id = chatId, text = html, parse_mode = "HTML", disable_web_page_preview = true }
                : new { chat_id = chatId, text = html, parse_mode = "HTML", disable_web_page_preview = true, reply_markup = replyMarkup };
            using var r = await client.PostAsJsonAsync($"{baseUrl}/sendMessage", payload, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Telegram yaniti gonderilemedi: {Chat}", chatId); }
    }

    private async Task ReplyIdAsync(string baseUrl, HttpClient client, TgChat chat, CancellationToken ct)
    {
        var title = chat.Title ?? chat.Username ?? chat.FirstName ?? "(sohbet)";
        var text =
            "<b>Sohbet bilgisi</b>\n" +
            $"ID: <code>{chat.Id}</code>\n" +
            $"Tur: {chat.Type}\n" +
            $"Baslik: {System.Net.WebUtility.HtmlEncode(title)}\n\n" +
            "Panelde <b>Dis ID</b> alanina bu ID'yi girebilirsin.";
        try
        {
            using var r = await client.PostAsJsonAsync($"{baseUrl}/sendMessage",
                new { chat_id = chat.Id, text, parse_mode = "HTML" }, ct);
            logger.LogInformation("/getid yaniti gonderildi: chat={Id} ({Type})", chat.Id, chat.Type);
        }
        catch (Exception ex) { logger.LogWarning(ex, "/getid yaniti gonderilemedi: {Id}", chat.Id); }
    }

    private sealed record TgUpdatesResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] List<TgUpdate>? Result);

    private sealed record TgUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TgMessage? Message,
        [property: JsonPropertyName("channel_post")] TgMessage? ChannelPost,
        [property: JsonPropertyName("callback_query")] TgCallbackQuery? CallbackQuery);

    private sealed record TgCallbackQuery(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("data")] string? Data,
        [property: JsonPropertyName("message")] TgMessage? Message);

    private sealed record TgMessage(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("chat")] TgChat? Chat);

    private sealed record TgChat(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("first_name")] string? FirstName);
}
