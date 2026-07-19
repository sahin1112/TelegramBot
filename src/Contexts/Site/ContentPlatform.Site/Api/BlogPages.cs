using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentPlatform.Abstractions;
using ContentPlatform.Site.Application;

namespace ContentPlatform.Site.Api;

/// <summary>
/// Sunucu tarafı (SSR) HTML üretimi + SEO çıktıları. Bağımlılık yok; saf string render.
/// Tema: mobil öncelikli, orange accent + serif başlık. Reklam slotları SiteOptions.AdsEnabled ile açılır.
/// </summary>
internal static class BlogPages
{
    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Abs(SiteOptions o, string path) => path.StartsWith("http") ? path : $"{o.BaseUrlTrimmed}{path}";

    // ---------- Tema (interpolasyonsuz: CSS/JS '{' içerir) ----------
    private const string BaseCss = """
    :root{--bg:#fbfbfa;--fg:#14171c;--muted:#6a7280;--line:#e9ebef;--soft:#f3f4f6;--accent:#ef5a24;--accent2:#ff7a45;--card:#fff;--ring:rgba(239,90,36,.14);
      --serif:"Iowan Old Style","Palatino Linotype",Palatino,Georgia,"Times New Roman",serif;--sans:system-ui,-apple-system,"Segoe UI",Roboto,Arial,sans-serif;--maxw:1200px;--tg:#229ED9;--yt:#FF0000}
    *{box-sizing:border-box}html{scroll-behavior:smooth}
    /* MOBİL: yatay taşmayı kökten kes. 'clip' scroll-konteyner OLUŞTURMAZ (sticky header bozulmaz);
       clip bilmeyen eski tarayıcı hidden'a düşer. */
    html,body{overflow-x:hidden;overflow-x:clip;max-width:100%}
    body{margin:0;font-family:var(--sans);background:var(--bg);color:var(--fg);line-height:1.7;-webkit-font-smoothing:antialiased}
    a{color:inherit;text-decoration:none}img{max-width:100%;display:block}
    /* Grid/flex çocukları uzun kelimede kolonu şişirmesin */
    .card,.lc,.rcard,.article-col,.side{min-width:0}
    .card .t,.card .d,.lc .tt,.rcard .t{overflow-wrap:break-word}
    .wrap{max-width:var(--maxw);margin:0 auto;padding:0 24px}.serif{font-family:var(--serif)}
    .progress{position:fixed;top:0;left:0;height:3px;width:0;background:var(--accent);z-index:60;transition:width .1s linear}
    .ad{display:flex;align-items:center;justify-content:center;min-height:var(--h,90px);border:1px solid var(--line);border-radius:14px;background:var(--soft);color:var(--muted);font-size:12px;margin:22px 0;overflow:hidden}
    .ad.box{--h:250px}.ad.rail{--h:600px}
    /* header */
    header.site{position:sticky;top:0;z-index:50;background:color-mix(in srgb,var(--bg) 90%,transparent);backdrop-filter:saturate(1.4) blur(12px);border-bottom:1px solid var(--line)}
    header.site .bar{display:flex;align-items:center;gap:26px;height:76px}
    .logo{font-family:var(--serif);font-weight:700;font-size:25px;letter-spacing:-.015em;white-space:nowrap;display:flex;align-items:center;gap:2px}.logo .dot{color:var(--accent)}
    header nav.main{display:flex;gap:26px;margin:0 auto}header nav.main a{color:var(--fg);font-size:15px;font-weight:500;opacity:.82}header nav.main a:hover{opacity:1;color:var(--accent)}
    .icons{display:flex;gap:8px}.iconbtn{width:42px;height:42px;border-radius:11px;border:1px solid var(--line);background:var(--card);display:grid;place-items:center;cursor:pointer;color:var(--fg)}.iconbtn.menu{display:none}
    .iconbtn svg{width:19px;height:19px}
    /* hero */
    .hero{display:grid;grid-template-columns:1.02fr .98fr;gap:48px;align-items:center;padding:52px 0 34px}
    .eyebrow{color:var(--accent);font-weight:700;font-size:12.5px;letter-spacing:.14em;text-transform:uppercase}
    .hero h1{font-family:var(--serif);font-size:clamp(34px,4.6vw,58px);line-height:1.04;letter-spacing:-.022em;margin:16px 0 18px;font-weight:700}
    .hero p.sub{color:var(--muted);font-size:18px;max-width:44ch;margin:0 0 26px}
    .btns{display:flex;gap:14px;flex-wrap:wrap;align-items:center}
    .btn{display:inline-flex;align-items:center;gap:9px;padding:14px 24px;border-radius:32px;font-weight:600;font-size:15px;cursor:pointer;border:1px solid transparent;transition:.15s}
    .btn.pri{background:var(--accent);color:#fff;box-shadow:0 10px 24px -10px var(--accent)}.btn.pri:hover{filter:brightness(1.05)}
    .btn.ghost{color:var(--fg)}.btn.ghost:hover{color:var(--accent)}
    .hero-nav{display:flex;align-items:center;gap:22px;margin-top:40px}
    .hero-nav .ey{color:var(--muted);font-size:11.5px;letter-spacing:.14em;text-transform:uppercase;font-weight:700}
    .dots{display:flex;align-items:center;gap:10px;font-size:13px;color:var(--muted)}.dots b{color:var(--fg)}.dots i{width:22px;height:1px;background:var(--line);display:inline-block}
    .harrows{display:flex;gap:8px;margin-left:6px}.harrows button{width:42px;height:42px;border-radius:50%;border:1px solid var(--line);background:var(--card);color:var(--fg);cursor:pointer;font-size:16px}
    .hero-media{position:relative;aspect-ratio:1/1;max-width:520px;margin-left:auto;width:100%}
    .hero-media .ring{position:absolute;inset:0;border-radius:50%;border:1.5px solid var(--accent);opacity:.5;transform:rotate(-12deg) scale(1.02)}
    .hero-media .disc{position:absolute;inset:14px;border-radius:50%;overflow:hidden;background:radial-gradient(120% 120% at 30% 20%,#f0f0ee,#dcdcda);box-shadow:0 40px 90px -50px rgba(0,0,0,.5)}
    .hero-media .disc img{width:100%;height:100%;object-fit:cover}
    .feat-card{position:absolute;right:-6px;bottom:22px;width:min(64%,320px);background:var(--card);border:1px solid var(--line);border-radius:18px;padding:20px;box-shadow:0 30px 70px -34px rgba(0,0,0,.45)}
    .feat-card .k{color:var(--accent);font-size:11px;font-weight:700;letter-spacing:.09em;text-transform:uppercase}
    .feat-card .t{font-family:var(--serif);font-size:21px;line-height:1.22;margin:9px 0 14px}
    .feat-card .m{display:flex;align-items:center;justify-content:space-between;color:var(--muted);font-size:12.5px}
    .feat-card .go{width:40px;height:40px;border-radius:50%;background:var(--accent);color:#fff;display:grid;place-items:center;flex:none}
    /* section head */
    .sec-h{display:flex;align-items:baseline;justify-content:space-between;margin:42px 0 18px;gap:16px}.sec-h h2{font-family:var(--serif);font-size:28px;margin:0}.sec-h a{color:var(--accent);font-weight:600;font-size:14px;white-space:nowrap}
    .ey{color:var(--muted);font-size:12px;letter-spacing:.12em;text-transform:uppercase;font-weight:700}
    /* social strip */
    .social{border-top:1px solid var(--line);border-bottom:1px solid var(--line);padding:26px 0;display:grid;grid-template-columns:230px 1fr;gap:28px;align-items:center;margin-top:12px}
    .social .lead .ey{margin-bottom:10px}.social .lead h3{font-family:var(--serif);font-size:22px;margin:0 0 4px}.social .lead p{margin:0 0 16px;color:var(--muted);font-size:14px}
    .social .lead a.all{display:inline-flex;gap:8px;align-items:center;border:1px solid var(--accent);color:var(--accent);border-radius:24px;padding:9px 16px;font-weight:600;font-size:13px}
    .chan{display:grid;grid-template-columns:repeat(5,1fr);gap:14px}
    .ch{border:1px solid var(--line);border-radius:16px;padding:16px;background:var(--card)}
    .ch .top{display:flex;align-items:center;gap:9px}.ch .ic{width:34px;height:34px;border-radius:10px;display:grid;place-items:center;flex:none;color:#fff}.ch .ic svg{width:19px;height:19px}
    .ch .nm{font-weight:700;font-size:13.5px;line-height:1.1}.ch .hd{color:var(--muted);font-size:11px}
    .ch .n{font-size:24px;font-weight:800;margin:12px 0 1px;letter-spacing:-.02em}.ch .lbl{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.04em}
    .ch a.f{display:block;margin-top:12px;text-align:center;padding:9px;border-radius:10px;color:#fff;font-weight:600;font-size:13px}
    /* live feed */
    .live-h{display:flex;align-items:center;justify-content:space-between;margin:40px 0 14px}
    .live-h .ey{display:flex;align-items:center;gap:9px}.live-h .ey::before{content:"";width:8px;height:8px;border-radius:50%;background:var(--accent);box-shadow:0 0 0 0 var(--ring);animation:pulse 1.8s infinite}
    @keyframes pulse{0%{box-shadow:0 0 0 0 var(--ring)}70%{box-shadow:0 0 0 8px transparent}100%{box-shadow:0 0 0 0 transparent}}
    .live{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(230px,1fr);gap:16px;overflow-x:auto;scroll-snap-type:x mandatory;padding-bottom:8px;scrollbar-width:thin}
    .lc{scroll-snap-align:start;border:1px solid var(--line);border-radius:16px;background:var(--card);overflow:hidden;display:flex;flex-direction:column}
    .lc .h{display:flex;align-items:center;gap:8px;padding:12px 13px 8px}.lc .h .ic{width:22px;height:22px;border-radius:7px;display:grid;place-items:center;color:#fff;flex:none}.lc .h .ic svg{width:13px;height:13px}
    .lc .h .nm{font-size:12.5px;font-weight:600}.lc .h .tm{margin-left:auto;color:var(--muted);font-size:11.5px}
    .lc .tt{padding:0 13px;margin-bottom:10px;font-size:14px;font-weight:600;line-height:1.35;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;height:calc(14px*1.35*2)}
    .lc .im{aspect-ratio:2/1;background:var(--soft);width:100%;object-fit:cover;object-position:center}
    .lc .st{display:flex;gap:16px;padding:10px 13px;color:var(--muted);font-size:12.5px}.lc .st span{display:flex;align-items:center;gap:5px}
    /* cards grid */
    .grid3{display:grid;grid-template-columns:repeat(3,1fr);gap:24px}
    .card{border:1px solid var(--line);border-radius:18px;overflow:hidden;background:var(--card);display:flex;flex-direction:column;transition:transform .22s,box-shadow .22s,border-color .22s}
    .card:hover{transform:translateY(-4px);box-shadow:0 22px 44px -22px rgba(0,0,0,.22);border-color:#e4d9cf}
    .card .im{transition:transform .35s}.card:hover .im{transform:scale(1.035)}
    .lc{transition:transform .22s,box-shadow .22s}.lc:hover{transform:translateY(-3px);box-shadow:0 16px 34px -20px rgba(0,0,0,.2)}
    .mr{display:grid;grid-template-columns:1fr 1fr;gap:4px 34px;margin:6px 0 10px}
    .mr a{display:flex;gap:16px;align-items:flex-start;padding:14px 10px;border-radius:12px;border-bottom:1px solid var(--line)}
    .mr a:hover{background:var(--soft)}
    .mr .no{font-family:var(--serif);font-size:30px;font-weight:700;color:var(--accent);opacity:.85;line-height:1;min-width:44px}
    .mr .t{font-weight:600;line-height:1.35;font-size:15.5px}.mr .m{color:var(--muted);font-size:12.5px;margin-top:4px}
    .pn{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin:26px 0 6px}
    .pn a{border:1px solid var(--line);border-radius:14px;padding:14px 16px;background:var(--card);transition:.15s}
    .pn a:hover{border-color:var(--accent)}
    .pn .lbl{color:var(--muted);font-size:11.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:700}
    .pn .tt{font-weight:600;line-height:1.35;margin-top:6px;font-size:14.5px}
    .pn a.next{text-align:right}
    .backtop{position:fixed;right:18px;bottom:18px;width:44px;height:44px;border-radius:50%;border:1px solid var(--line);background:var(--card);color:var(--fg);cursor:pointer;font-size:18px;box-shadow:0 10px 26px -12px rgba(0,0,0,.3);opacity:0;pointer-events:none;transition:.25s;z-index:55}
    .backtop.on{opacity:1;pointer-events:auto}
    @media(max-width:640px){.mr{grid-template-columns:1fr}.pn{grid-template-columns:1fr}}
    .card:hover{border-color:color-mix(in srgb,var(--accent) 40%,var(--line));transform:translateY(-3px);box-shadow:0 24px 50px -36px rgba(0,0,0,.4)}
    .card .im{aspect-ratio:2/1;background:var(--soft);width:100%;object-fit:cover;object-position:center}.card .p{padding:16px 17px;display:flex;flex-direction:column;gap:8px;flex:1}
    .card .k{color:var(--accent);font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em}.card .t{font-family:var(--serif);font-size:20px;line-height:1.25;margin:0;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}.card .d{color:var(--muted);font-size:14px;margin:0;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}.card .m{color:var(--muted);font-size:12.5px;margin-top:auto}
    .card.big{grid-column:span 2;flex-direction:row}.card.big .im{width:46%;aspect-ratio:2/1;min-height:0;object-fit:cover;align-self:center;border-radius:12px;margin:14px 0 14px 14px}.card.big .p{justify-content:center}.card.big .t{font-size:25px}
    .nl{margin:48px 0 8px;border:1px solid var(--line);border-radius:22px;background:linear-gradient(135deg,var(--ring),transparent);padding:32px}
    .nl h3{font-family:var(--serif);font-size:26px;margin:0 0 6px}.nl p{margin:0 0 18px;color:var(--muted)}
    .nl form{display:flex;gap:10px;max-width:480px;flex-wrap:wrap}.nl input{flex:1;min-width:210px;padding:14px 15px;border:1px solid var(--line);border-radius:12px;background:var(--bg);color:var(--fg);font:inherit}.nl button{padding:14px 24px;border:none;border-radius:12px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}
    .nl .ok{color:var(--accent);font-weight:600;margin-top:10px}
    .crumbs{font-size:13px;color:var(--muted);padding:16px 0 4px;display:flex;gap:8px;flex-wrap:wrap}.crumbs a:hover{color:var(--accent)}.crumbs span{opacity:.5}
    .glayout{display:grid;grid-template-columns:minmax(0,1fr) 320px;gap:48px;padding:8px 0 40px;align-items:start}
    .article-col{min-width:0}
    .with-rail{display:grid;grid-template-columns:52px minmax(0,1fr);gap:22px}
    .share-rail{position:sticky;top:96px;display:flex;flex-direction:column;gap:10px;align-self:start}
    .share-rail a{width:42px;height:42px;border-radius:50%;border:1px solid var(--line);background:var(--card);display:grid;place-items:center;color:var(--muted);transition:.15s}
    .share-rail a:hover{color:#fff;background:var(--accent);border-color:var(--accent);transform:translateY(-2px)}
    .share-rail svg{width:17px;height:17px}
    .kicker{color:var(--accent);font-weight:700;font-size:13px;letter-spacing:.08em;text-transform:uppercase}
    h1.title{font-family:var(--serif);font-weight:700;font-size:clamp(30px,5vw,46px);line-height:1.12;letter-spacing:-.015em;margin:10px 0 14px}
    .dek{font-size:19px;color:var(--muted);line-height:1.6;margin:0 0 22px;max-width:60ch}
    .byline{display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:14px 0;border-top:1px solid var(--line);border-bottom:1px solid var(--line)}
    .avatar{width:44px;height:44px;border-radius:50%;background:#14171c;color:#fff;display:grid;place-items:center;font-weight:700;font-family:var(--serif)}
    .byline .who{font-weight:600;display:flex;align-items:center;gap:6px}.byline .vf{color:var(--accent)}.byline .m{color:var(--muted);font-size:14px}
    .cover{width:100%;height:auto;border-radius:18px;margin:22px 0 6px;background:var(--soft);display:block}
    /* Makale tipografisi: SOLA YASLI (web okuma standardı — iki yana yaslama tirajsız boşluklar/nehirler
       yaratır, özellikle mobilde). Kenar düzgünlüğü için Türkçe heceleme + akıllı satır kırma açık. */
    .body{font-size:18px}.body p{margin:18px 0;text-wrap:pretty;hyphens:auto;-webkit-hyphens:auto}.body h2{font-family:var(--serif);font-size:27px;line-height:1.25;margin:36px 0 10px;scroll-margin-top:92px;letter-spacing:-.01em}
    .body h3{font-family:var(--serif);font-size:21px;margin:26px 0 8px;scroll-margin-top:92px}.body a{color:var(--accent);text-decoration:underline;text-underline-offset:2px}
    .body img{border-radius:14px;margin:20px 0}.body ul,.body ol{margin:16px 0;padding-left:22px}.body li{margin:7px 0}
    /* Makale gövdesi mobil taşma korumaları: uzun link/kelime kırılır; iframe/video/tablo kolona sığar */
    .body{overflow-wrap:break-word}.body iframe,.body video{max-width:100%}
    .body table{display:block;max-width:100%;overflow-x:auto;border-collapse:collapse}
    .body pre{max-width:100%;overflow-x:auto}
    .body blockquote{margin:24px 0;padding:6px 20px;border-left:4px solid var(--accent);font-family:var(--serif);font-size:21px;line-height:1.5}
    .tags{display:flex;flex-wrap:wrap;gap:9px;margin:30px 0 6px}.tags a{background:var(--soft);border:1px solid var(--line);border-radius:20px;padding:6px 14px;font-size:13px;color:var(--muted)}.tags a:hover{color:var(--accent);border-color:var(--accent)}
    .share-inline{display:none;gap:10px;margin:22px 0}.share-inline a{flex:1;text-align:center;padding:11px;border:1px solid var(--line);border-radius:10px;color:var(--muted);font-size:14px;font-weight:600}
    .side{display:flex;flex-direction:column;gap:26px;position:sticky;top:96px}
    .panel{border:1px solid var(--line);border-radius:18px;background:var(--card);overflow:hidden}
    .panel h3{margin:0;padding:15px 18px;font-size:13px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted);border-bottom:1px solid var(--line)}
    .toc ol{list-style:none;margin:0;padding:8px 8px 12px;counter-reset:t}.toc li{counter-increment:t}
    .toc a{display:flex;gap:12px;padding:9px 12px;border-radius:10px;color:var(--muted);font-size:14.5px;line-height:1.4}.toc a::before{content:counter(t,decimal-leading-zero);color:var(--accent);font-weight:700}
    .toc a:hover{background:var(--soft);color:var(--fg)}.toc a.active{background:var(--ring);color:var(--fg);font-weight:600}.toc li.sub a{padding-left:30px;font-size:14px}
    .news{padding:20px 18px}.news h4{font-family:var(--serif);font-size:20px;margin:0 0 6px}.news p{margin:0 0 12px;color:var(--muted);font-size:14px}
    .news input{width:100%;padding:11px 12px;border:1px solid var(--line);border-radius:10px;background:var(--bg);color:var(--fg);font:inherit;margin-bottom:9px}.news button{width:100%;padding:11px;border:none;border-radius:10px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}.news small{display:block;text-align:center;color:var(--muted);margin-top:8px;font-size:12px}
    .related{border-top:1px solid var(--line);margin-top:40px;padding-top:24px}.related h2{font-family:var(--serif);font-size:24px;margin:0 0 16px}
    .rgrid{display:grid;grid-template-columns:repeat(3,1fr);gap:18px}.rcard{border:1px solid var(--line);border-radius:14px;overflow:hidden;background:var(--card)}.rcard .im{aspect-ratio:2/1;background:var(--soft);width:100%;object-fit:cover;object-position:center}.rcard .p{padding:12px 14px}.rcard .k{color:var(--accent);font-size:11px;font-weight:700;text-transform:uppercase}.rcard .t{font-weight:600;line-height:1.35;margin:5px 0 0;font-family:var(--serif);display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}
    .comments{border-top:1px solid var(--line);margin-top:40px;padding-top:24px}.comments h2{font-family:var(--serif);font-size:24px;margin:0 0 6px}
    .cmt{display:flex;gap:12px;padding:16px 0;border-bottom:1px solid var(--line)}.cmt .av{width:38px;height:38px;border-radius:50%;background:var(--soft);color:var(--muted);display:grid;place-items:center;font-weight:700;flex:none}.cmt .who{font-weight:600}.cmt .when{color:var(--muted);font-size:13px}.cmt p{margin:4px 0 0}
    form.cmt-form{display:grid;gap:10px;max-width:560px;margin-top:18px}form.cmt-form .row{display:grid;grid-template-columns:1fr 1fr;gap:10px}
    form.cmt-form input,form.cmt-form textarea{font:inherit;padding:11px 12px;border:1px solid var(--line);border-radius:10px;background:var(--bg);color:var(--fg)}form.cmt-form textarea{min-height:110px;resize:vertical}form.cmt-form button{justify-self:start;background:var(--accent);color:#fff;border:none;border-radius:10px;padding:11px 22px;font:inherit;font-weight:600;cursor:pointer}
    .note{color:var(--accent);font-weight:600}
    footer.site{border-top:1px solid var(--line);margin-top:52px;padding:30px 0;color:var(--muted);font-size:14px}footer.site .cols{display:flex;justify-content:space-between;gap:20px;flex-wrap:wrap}
    .anchor{position:fixed;left:0;right:0;bottom:0;z-index:55;display:none;background:var(--card);border-top:1px solid var(--line);padding:8px 12px;align-items:center;gap:10px}.anchor .ad{margin:0;flex:1;--h:56px}.anchor .x{width:26px;height:26px;border:1px solid var(--line);border-radius:7px;background:var(--bg);color:var(--muted);cursor:pointer;flex:none}
    /* mini app: kanal chrome'unu sadelestir */
    body.miniapp header.site,body.miniapp footer.site,body.miniapp .anchor,body.miniapp #cc{display:none!important}
    body.miniapp main{padding-top:8px}
    @media(max-width:1024px){.hero{grid-template-columns:1fr;gap:30px}.hero-media{max-width:420px;margin:0 auto}.social{grid-template-columns:1fr}.chan{grid-template-columns:repeat(3,1fr)}.grid3{grid-template-columns:repeat(2,1fr)}.glayout{grid-template-columns:1fr;gap:8px}.side{position:static}.side .rail-ad{display:none}.rgrid{grid-template-columns:repeat(2,1fr)}header nav.main{display:none}
      /* Mobil menü: hamburger görünür; menü header'ın ALTINA tam genişlik DİKEY panel olarak açılır
         (eski davranış nav'ı yatay bar içine açıp header'ı kırıyordu). */
      .iconbtn.menu{display:grid}
      header nav.main{position:absolute;top:100%;left:0;right:0;background:var(--card);border-bottom:1px solid var(--line);flex-direction:column;gap:0;margin:0;padding:6px 20px 12px;box-shadow:0 26px 44px -30px rgba(0,0,0,.4)}
      header nav.main.open{display:flex}
      header nav.main a{padding:12px 4px;border-bottom:1px solid var(--line);font-size:15.5px;opacity:1}
      header nav.main a:last-child{border-bottom:none}}
    @media(max-width:640px){.iconbtn.menu{display:grid}.chan{grid-template-columns:repeat(2,1fr)}.grid3{grid-template-columns:1fr}.card.big{grid-column:span 1;flex-direction:column}.card.big .im{width:100%;min-height:0;aspect-ratio:2/1;margin:0;border-radius:0}.with-rail{grid-template-columns:1fr}.share-rail{display:none}.share-inline{display:flex}.body{font-size:17px}.rgrid{grid-template-columns:1fr}.anchor{display:flex}main{padding-bottom:76px}form.cmt-form .row{grid-template-columns:1fr}.feat-card{position:static;width:auto;margin-top:16px}.hero-media{aspect-ratio:auto}.hero-media .disc{position:static;inset:auto;aspect-ratio:1/1}.hero-media .ring{display:none}}
    """;

    private const string BaseJs = """
    (function(){
      // Mini app (Telegram) icinde mi? ?ma=1 ya da Telegram WebApp -> sadelestir
      try{var ma=new URLSearchParams(location.search).get('ma')==='1'||(window.Telegram&&Telegram.WebApp&&Telegram.WebApp.initData);
        if(ma){document.body.classList.add('miniapp');try{Telegram.WebApp.ready();Telegram.WebApp.expand();}catch(e){}}}catch(e){}
      // Bulten formu: sayfa yenilemeden gonder
      [].forEach.call(document.querySelectorAll('form[data-nl]'),function(f){f.addEventListener('submit',function(ev){ev.preventDefault();
        var em=f.querySelector('input[type=email]');var btn=f.querySelector('button');if(!em||!em.value)return;
        btn.disabled=true;var ot=btn.textContent;btn.textContent='Gonderiliyor...';
        var fd=new FormData();fd.append('email',em.value);
        fetch('/blog/subscribe',{method:'POST',headers:{'X-Requested-With':'fetch'},body:fd}).then(function(r){return r.json();}).then(function(d){
          f.innerHTML='<div class="ok">'+(d&&d.ok?'Tesekkurler! Kaydin alindi.':'Gecerli bir e-posta gir.')+'</div>';
        }).catch(function(){btn.disabled=false;btn.textContent=ot;});
      });});
      var pb=document.getElementById('progress');
      if(pb){addEventListener('scroll',function(){var h=document.documentElement;var st=h.scrollTop||document.body.scrollTop;var sh=(h.scrollHeight)-h.clientHeight;pb.style.width=(sh>0?st/sh*100:0)+'%';},{passive:true});}
      var bt=document.getElementById('backtop');
      if(bt){addEventListener('scroll',function(){bt.classList.toggle('on',(document.documentElement.scrollTop||document.body.scrollTop)>600);},{passive:true});
        bt.addEventListener('click',function(){scrollTo({top:0,behavior:'smooth'});});}
      var links=[].slice.call(document.querySelectorAll('#toc a'));
      var heads=links.map(function(a){return document.getElementById(a.getAttribute('href').slice(1));}).filter(Boolean);
      if('IntersectionObserver' in window && heads.length){
        var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){links.forEach(function(l){l.classList.toggle('active',l.getAttribute('href')==='#'+e.target.id);});}});},{rootMargin:'-80px 0px -70% 0px'});
        heads.forEach(function(h){io.observe(h);});
      }
    })();
    """;

    // ---------- İkonlar (inline SVG) ----------
    private const string IconSearch = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2'><circle cx='11' cy='11' r='7'/><path d='M21 21l-4.3-4.3'/></svg>";
    private const string IconMenu = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2'><path d='M3 6h18M3 12h18M3 18h18'/></svg>";
    private const string IconHeart = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8'><path d='M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.6l-1-1a5.5 5.5 0 1 0-7.8 7.8l1 1L12 21l7.8-7.6 1-1a5.5 5.5 0 0 0 0-7.8z'/></svg>";
    private const string IconChat = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8'><path d='M21 11.5a8.4 8.4 0 0 1-11.9 7.6L3 21l1.9-6.1A8.5 8.5 0 1 1 21 11.5z'/></svg>";
    private const string IconEye = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8'><path d='M1 12s4-7 11-7 11 7 11 7-4 7-11 7-11-7-11-7z'/><circle cx='12' cy='12' r='3'/></svg>";
    // Marka ikonları (beyaz, zemin renkli kap içinde)
    private const string SvgTelegram = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M9.8 15.6l-.4 4.1c.5 0 .8-.2 1.1-.5l2.6-2.5 5.4 3.9c1 .5 1.7.3 1.9-.9l3.5-16.3c.3-1.4-.5-2-1.5-1.6L1.1 9.3C-.3 9.8-.2 10.6.9 11l5.1 1.6L18.4 5c.6-.4 1.1-.2.7.2z'/></svg>";
    private const string SvgX = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M18.2 2h3.3l-7.2 8.3L23 22h-6.6l-5.2-6.8L5.3 22H2l7.7-8.8L1.5 2h6.8l4.7 6.2zm-1.2 18h1.8L7.1 3.9H5.2z'/></svg>";
    private const string SvgInstagram = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M12 2.2c3.2 0 3.6 0 4.9.1 1.2.1 1.8.3 2.2.4.6.2 1 .5 1.4.9.4.4.7.8.9 1.4.1.4.3 1 .4 2.2.1 1.3.1 1.7.1 4.9s0 3.6-.1 4.9c-.1 1.2-.3 1.8-.4 2.2-.2.6-.5 1-.9 1.4-.4.4-.8.7-1.4.9-.4.1-1 .3-2.2.4-1.3.1-1.7.1-4.9.1s-3.6 0-4.9-.1c-1.2-.1-1.8-.3-2.2-.4-.6-.2-1-.5-1.4-.9-.4-.4-.7-.8-.9-1.4-.1-.4-.3-1-.4-2.2C2.2 15.6 2.2 15.2 2.2 12s0-3.6.1-4.9c.1-1.2.3-1.8.4-2.2.2-.6.5-1 .9-1.4.4-.4.8-.7 1.4-.9.4-.1 1-.3 2.2-.4C8.4 2.2 8.8 2.2 12 2.2zM12 6.9a5.1 5.1 0 1 0 0 10.2 5.1 5.1 0 0 0 0-10.2zm0 8.4a3.3 3.3 0 1 1 0-6.6 3.3 3.3 0 0 1 0 6.6zm5.3-8.6a1.2 1.2 0 1 0 0 2.4 1.2 1.2 0 0 0 0-2.4z'/></svg>";
    private const string SvgThreads = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M17.3 11.2c-.1 0-.2-.1-.3-.1-.2-3-1.8-4.7-4.5-4.7-1.6 0-3 .7-3.8 2l1.5 1c.6-.9 1.5-1.1 2.3-1.1 1.4 0 2.4.9 2.6 2.4-.6-.1-1.1-.2-1.8-.2-2.5 0-4.1 1.4-4 3.4.1 1.9 1.7 2.9 3.4 2.9 1.7 0 3.4-1 3.7-3.4.6.4 1 .9 1.3 1.6.4 1.1.5 2.9-1 4.4-1.3 1.3-2.9 1.9-5.2 1.9-2.6 0-4.5-.8-5.8-2.5C4.6 17.4 4 15.3 4 12.5s.6-4.9 1.7-6.4C7 4.4 8.9 3.6 11.5 3.6c2.6 0 4.5.8 5.9 2.5.7.8 1.1 1.9 1.4 3.1l1.9-.5c-.3-1.5-.9-2.8-1.8-3.9C18.9 2.6 16.5 1.6 13.4 1.6h-.1C9.4 1.6 6.9 3 5.3 5.5 3.9 7.5 3.2 10 3.2 13v.1c0 3 .7 5.5 2.1 7.4 1.6 2.2 4.1 3.3 7.4 3.3h.1c2.9 0 5-.8 6.7-2.4 2.2-2.2 2.1-5 1.4-6.7-.5-1.3-1.5-2.3-2.8-3zm-4.5 4.9c-.9 0-1.9-.4-1.9-1.4 0-.7.5-1.5 2.2-1.5.6 0 1.1.1 1.7.2-.2 1.9-1.1 2.7-2 2.7z'/></svg>";
    private const string SvgWhatsApp = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M12 2a10 10 0 0 0-8.6 15l-1.3 4.7 4.8-1.3A10 10 0 1 0 12 2zm5.8 14.2c-.2.7-1.4 1.3-2 1.4-.5.1-1.2.1-1.9-.1-.4-.1-1-.3-1.7-.6-3-1.3-4.9-4.3-5-4.5-.2-.2-1.2-1.6-1.2-3s.7-2.1 1-2.4c.2-.3.5-.4.7-.4h.5c.2 0 .4 0 .6.5l.8 2c.1.2.1.4 0 .5l-.4.6-.3.3c-.1.1-.3.3-.1.6.1.3.7 1.1 1.4 1.8.9.8 1.7 1.1 2 1.2.2.1.4.1.5-.1l.7-.8c.2-.2.4-.2.6-.1l1.9.9c.3.1.4.2.5.3.1.3.1.6-.1 1.4z'/></svg>";
    private const string SvgLinkedIn = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M4.98 3.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5zM3 9h4v12H3zM9 9h3.8v1.7h.1c.5-1 1.8-2 3.7-2 4 0 4.7 2.6 4.7 6V21H21v-5.6c0-1.3 0-3-1.9-3s-2.1 1.4-2.1 2.9V21h-4z'/></svg>";
    private const string IconMail = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8'><rect x='2' y='4' width='20' height='16' rx='3'/><path d='M3 6l9 7 9-7'/></svg>";
    private const string IconLink = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8'><path d='M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1'/><path d='M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1'/></svg>";
    private const string SvgYoutube = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M23 7.5a3 3 0 0 0-2.1-2.1C19 4.9 12 4.9 12 4.9s-7 0-8.9.5A3 3 0 0 0 1 7.5 31 31 0 0 0 .5 12 31 31 0 0 0 1 16.5a3 3 0 0 0 2.1 2.1c1.9.5 8.9.5 8.9.5s7 0 8.9-.5a3 3 0 0 0 2.1-2.1A31 31 0 0 0 23.5 12 31 31 0 0 0 23 7.5zM9.8 15.3V8.7l5.7 3.3z'/></svg>";
    private const string SvgTikTok = "<svg viewBox='0 0 24 24' fill='currentColor'><path d='M12.9.5c.6 3 2.5 4.9 5.6 5.1v3.3c-1.9.1-3.6-.5-5.6-1.7v7.6c0 4.6-4.2 7.6-8.3 6.1-3-1.1-4.5-4.5-3.2-7.4 1.2-2.7 4-4.1 7.2-3.6v3.4c-.5-.1-1-.2-1.5-.1-1.7.1-2.9 1.4-2.8 3 .1 1.6 1.5 2.8 3.1 2.7 1.6-.1 2.7-1.3 2.7-3.1V.5h2.8z'/></svg>";

    private static string NavLinks(SiteOptions o)
    {
        var items = o.Nav.Count > 0
            ? o.Nav
            : o.NavCategories.Select(c => new SiteNavItem(c, "/etiket/" + Uri.EscapeDataString(c))).ToList();
        var sb = new StringBuilder();
        foreach (var it in items)
            sb.Append("<a href=\"").Append(Enc(it.Href)).Append("\">").Append(Enc(it.Label)).Append("</a>");
        return sb.ToString();
    }

    private static string Layout(SiteOptions o, string headExtra, string bodyInner)
    {
        var adHead = (o.AdsEnabled && !string.IsNullOrWhiteSpace(o.AdSenseClient))
            ? "<script async src=\"https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=" + Enc(o.AdSenseClient!) + "\" crossorigin=\"anonymous\"></script>"
            : "";
        var anchor = o.AdsEnabled
            ? "<div class=\"anchor\" id=\"anchor\">" + AdSlot(o, "MobileAnchor", "") + "<button class=\"x\" onclick=\"document.getElementById('anchor').style.display='none'\">✕</button></div>"
            : "";
        return "<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
            + FaviconHead(o) + headExtra + Preconnect(o) + GscMeta(o) + AnalyticsHead(o) + adHead + "<style>" + BaseCss + "</style></head><body>"
            + GtmNoscript(o)
            + "<div class=\"progress\" id=\"progress\"></div>"
            + "<header class=\"site\"><div class=\"wrap bar\"><a class=\"logo\" href=\"/blog\">" + Enc(o.SiteName) + " <span class=\"dot\">•</span></a>"
            + "<nav class=\"main\">" + NavLinks(o) + "</nav>"
            + "<div class=\"icons\"><a class=\"iconbtn\" href=\"/ara\" title=\"Ara\">" + IconSearch + "</a>"
            + "<button class=\"iconbtn menu\" title=\"Menü\" onclick=\"document.querySelector('nav.main').classList.toggle('open')\">" + IconMenu + "</button></div></div></header>"
            + "<main class=\"wrap\">" + bodyInner + "</main>"
            + "<footer class=\"site\"><div class=\"wrap cols\"><div>© " + DateTimeOffset.UtcNow.Year + " " + Enc(o.SiteName) + " · " + Enc(o.Description) + "</div>"
            + "<div><a href=\"/hakkimizda\">Hakkımızda</a> · <a href=\"/iletisim\">İletişim</a> · <a href=\"/yazilar\">Tüm Yazılar</a> · <a href=\"/sosyal\">Sosyal</a> · <a href=\"/feed.xml\">RSS</a> · <a href=\"/sitemap.xml\">Site haritası</a>" + FooterLinks(o) + "</div></div></footer>"
            + ConsentBanner(o) + anchor + "<button class=\"backtop\" id=\"backtop\" aria-label=\"Yukarı çık\">↑</button><script>" + BaseJs + "</script></body></html>";
    }

    /// <summary>Favicon (satır içi SVG — dosya gerekmez, 404 olmaz) + mobil tema rengi.</summary>
    private static string FaviconHead(SiteOptions o)
    {
        var letter = string.IsNullOrWhiteSpace(o.SiteName) ? "H" : o.SiteName.Trim()[..1].ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        // SVG'nin TAMAMI URL-encode edilir — kısmi kaçış tarayıcıda render edilmiyor (test edildi).
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><rect width='64' height='64' rx='14' fill='#E8552E'/><text x='32' y='44' font-family='Georgia,serif' font-size='38' font-weight='bold' text-anchor='middle' fill='white'>" + letter + "</text></svg>";
        return "<link rel=\"icon\" type=\"image/svg+xml\" href=\"data:image/svg+xml," + Uri.EscapeDataString(svg) + "\">"
             + "<link rel=\"manifest\" href=\"/site.webmanifest\">"
             + "<meta name=\"theme-color\" content=\"#ffffff\">";
    }

    private static string Preconnect(SiteOptions o)
    {
        var a = !string.IsNullOrWhiteSpace(o.Ga4Id) || !string.IsNullOrWhiteSpace(o.GtmId);
        var ads = o.AdsEnabled && !string.IsNullOrWhiteSpace(o.AdSenseClient);
        var sb = new StringBuilder();
        if (a) sb.Append("<link rel=\"preconnect\" href=\"https://www.googletagmanager.com\"><link rel=\"dns-prefetch\" href=\"https://www.google-analytics.com\">");
        if (ads) sb.Append("<link rel=\"preconnect\" href=\"https://pagead2.googlesyndication.com\" crossorigin><link rel=\"dns-prefetch\" href=\"https://googleads.g.doubleclick.net\">");
        return sb.ToString();
    }

    private static string GscMeta(SiteOptions o) =>
        string.IsNullOrWhiteSpace(o.GscVerification) ? "" : "<meta name=\"google-site-verification\" content=\"" + Enc(o.GscVerification) + "\">";

    /// <summary>GTM'nin ikinci parçası: &lt;body&gt; hemen ardına gelen noscript iframe (JS kapalı fallback + kurulum bütünlüğü).</summary>
    private static string GtmNoscript(SiteOptions o) =>
        string.IsNullOrWhiteSpace(o.GtmId) ? "" :
        "<noscript><iframe src=\"https://www.googletagmanager.com/ns.html?id=" + Enc(o.GtmId!) + "\" height=\"0\" width=\"0\" style=\"display:none;visibility:hidden\"></iframe></noscript>";

    private static string AnalyticsHead(SiteOptions o)
    {
        var ga = o.Ga4Id; var gtm = o.GtmId;
        if (string.IsNullOrWhiteSpace(ga) && string.IsNullOrWhiteSpace(gtm)) return "";
        var sb = new StringBuilder();
        sb.Append("<script>window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}");
        if (o.ConsentRequired) sb.Append("gtag('consent','default',{ad_storage:'denied',analytics_storage:'denied',ad_user_data:'denied',ad_personalization:'denied',wait_for_update:500});");
        sb.Append("</script>");
        if (!string.IsNullOrWhiteSpace(ga))
        {
            sb.Append("<script async src=\"https://www.googletagmanager.com/gtag/js?id=").Append(Enc(ga)).Append("\"></script>");
            sb.Append("<script>gtag('js',new Date());gtag('config','").Append(Enc(ga)).Append("');</script>");
        }
        if (!string.IsNullOrWhiteSpace(gtm))
        {
            sb.Append("<script>(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src='https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);})(window,document,'script','dataLayer','").Append(Enc(gtm)).Append("');</script>");
        }
        return sb.ToString();
    }

    private static string FooterLinks(SiteOptions o)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(o.PrivacyUrl)) sb.Append(" · <a href=\"").Append(Enc(o.PrivacyUrl)).Append("\">Gizlilik</a>");
        if (!string.IsNullOrWhiteSpace(o.CookieUrl)) sb.Append(" · <a href=\"").Append(Enc(o.CookieUrl)).Append("\">Çerezler</a>");
        return sb.ToString();
    }

    private static string ConsentBanner(SiteOptions o)
    {
        if (!o.ConsentRequired) return "";
        var trackers = !string.IsNullOrWhiteSpace(o.Ga4Id) || !string.IsNullOrWhiteSpace(o.GtmId) || (o.AdsEnabled && !string.IsNullOrWhiteSpace(o.AdSenseClient));
        if (!trackers) return "";
        var priv = string.IsNullOrWhiteSpace(o.PrivacyUrl) ? "" : " <a href=\"" + Enc(o.PrivacyUrl) + "\" style=\"color:var(--accent)\">Gizlilik</a>";
        return "<div id=\"cc\" style=\"position:fixed;left:12px;right:12px;bottom:12px;z-index:70;background:var(--card);border:1px solid var(--line);border-radius:14px;padding:14px 16px;display:none;box-shadow:0 10px 40px rgba(0,0,0,.2)\"><div style=\"display:flex;gap:12px;align-items:center;flex-wrap:wrap;justify-content:space-between\"><div style=\"font-size:14px;color:var(--muted);max-width:60ch\">Deneyimini iyilestirmek ve reklamlari kisisellestirmek icin cerez kullaniyoruz."
            + priv + "</div><div style=\"display:flex;gap:8px\"><button id=\"cc-no\" style=\"padding:9px 16px;border:1px solid var(--line);border-radius:9px;background:var(--bg);color:var(--fg);cursor:pointer\">Reddet</button><button id=\"cc-yes\" style=\"padding:9px 16px;border:none;border-radius:9px;background:var(--accent);color:#fff;cursor:pointer\">Kabul et</button></div></div></div>"
            + "<script>(function(){window.dataLayer=window.dataLayer||[];function g(){dataLayer.push(arguments);}function set(v){document.cookie='cc_consent='+v+';path=/;max-age=15552000';}function val(){var m=document.cookie.match(/cc_consent=(\\w+)/);return m?m[1]:'';}function apply(v){g('consent','update',{ad_storage:v,analytics_storage:v,ad_user_data:v,ad_personalization:v});}var el=document.getElementById('cc');if(val()){apply(val()==='granted'?'granted':'denied');}else{el.style.display='block';}document.getElementById('cc-yes').onclick=function(){set('granted');apply('granted');el.style.display='none';};document.getElementById('cc-no').onclick=function(){set('denied');apply('denied');el.style.display='none';};})();</script>";
    }

    private static string AdSlot(SiteOptions o, string placement, string cssClass, bool? allow = null)
    {
        if (!(allow ?? o.AdsEnabled)) return "";
        // Manuel reklam birimi YALNIZ hem yayıncı kimliği HEM slot ID varken basılır. Slotsuz <ins> DOLMAZ
        // (boş kutu + konsol hatası). Slot yoksa hiçbir şey basma → Otomatik reklamlar (Auto ads) yerleştirir.
        if (string.IsNullOrWhiteSpace(o.AdSenseClient) || string.IsNullOrWhiteSpace(o.AdSenseSlot)) return "";
        var sb = new StringBuilder();
        sb.Append("<div class=\"ad ").Append(cssClass).Append("\" data-placement=\"").Append(placement).Append("\">");
        sb.Append("<ins class=\"adsbygoogle\" style=\"display:block;width:100%\" data-ad-client=\"")
          .Append(Enc(o.AdSenseClient)).Append("\" data-ad-slot=\"").Append(Enc(o.AdSenseSlot!))
          .Append("\" data-ad-format=\"auto\" data-full-width-responsive=\"true\"></ins>")
          .Append("<script>(adsbygoogle=window.adsbygoogle||[]).push({});</script>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string SeoHead(SiteOptions o, string title, string description, string canonicalPath, string? imageUrl, string type, string? jsonLd)
    {
        var canonical = Abs(o, canonicalPath);
        var img = imageUrl is null ? "" : Abs(o, imageUrl);
        var sb = new StringBuilder();
        sb.Append($"<title>{Enc(title)}</title>");
        sb.Append($"<meta name=\"description\" content=\"{Enc(description)}\">");
        sb.Append($"<link rel=\"canonical\" href=\"{Enc(canonical)}\">");
        sb.Append($"<link rel=\"alternate\" hreflang=\"tr\" href=\"{Enc(canonical)}\">");
        sb.Append($"<link rel=\"alternate\" hreflang=\"x-default\" href=\"{Enc(canonical)}\">");
        sb.Append("<meta name=\"robots\" content=\"index,follow,max-image-preview:large,max-snippet:-1\">");
        sb.Append("<meta property=\"og:locale\" content=\"tr_TR\">");
        sb.Append($"<meta property=\"og:type\" content=\"{type}\"><meta property=\"og:title\" content=\"{Enc(title)}\">");
        sb.Append($"<meta property=\"og:description\" content=\"{Enc(description)}\"><meta property=\"og:url\" content=\"{Enc(canonical)}\">");
        sb.Append($"<meta property=\"og:site_name\" content=\"{Enc(o.SiteName)}\">");
        if (img.Length > 0) sb.Append($"<meta property=\"og:image\" content=\"{Enc(img)}\">");
        sb.Append($"<meta name=\"twitter:card\" content=\"{(img.Length > 0 ? "summary_large_image" : "summary")}\">");
        // X (Twitter) kanalı seçiliyse marka hesabını bildir (kart atıfı)
        var xUrl = o.HomeSocials.FirstOrDefault(s2 => s2.Platform == Platform.X)?.Url;
        if (xUrl is not null && HandleFromUrl(xUrl) is { } xh)
            sb.Append($"<meta name=\"twitter:site\" content=\"{Enc(xh)}\">");
        sb.Append($"<meta name=\"twitter:title\" content=\"{Enc(title)}\"><meta name=\"twitter:description\" content=\"{Enc(description)}\">");
        if (img.Length > 0) sb.Append($"<meta name=\"twitter:image\" content=\"{Enc(img)}\">");
        sb.Append($"<link rel=\"alternate\" type=\"application/rss+xml\" title=\"{Enc(o.SiteName)}\" href=\"{Enc(Abs(o, "/feed.xml"))}\">");
        if (jsonLd is not null) sb.Append($"<script type=\"application/ld+json\">{jsonLd}</script>");
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions JsonLdOpts = new()
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Anasayfa için Organization + WebSite yapısal verisi (marka/knowledge sinyali).</summary>
    private static string SiteJsonLd(SiteOptions o)
    {
        var baseUrl = o.BaseUrlTrimmed;
        var sameAs = new List<string>();
        // Öncelik: "Sosyal Hesaplar"da yayınla seçili kanallar (gerçek profiller = güçlü marka sinyali).
        foreach (var s in o.HomeSocials)
            if (!string.IsNullOrWhiteSpace(s.Url) && !sameAs.Contains(s.Url)) sameAs.Add(s.Url);
        if (sameAs.Count == 0)
            foreach (var u in new[] { o.TelegramUrl, o.XUrl, o.InstagramUrl, o.ThreadsUrl, o.YoutubeUrl })
                if (!string.IsNullOrWhiteSpace(u)) sameAs.Add(u!);
        var org = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Organization",
            ["name"] = o.SiteName, ["url"] = baseUrl, ["description"] = o.Description,
            ["logo"] = new Dictionary<string, object?> { ["@type"] = "ImageObject", ["url"] = Abs(o, "/logo.svg") }
        };
        if (sameAs.Count > 0) org["sameAs"] = sameAs;
        var web = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "WebSite",
            ["name"] = o.SiteName, ["url"] = baseUrl, ["inLanguage"] = "tr-TR",
            // Sitede arama var (/ara) → Google'a bildir (sitelinks searchbox sinyali)
            ["potentialAction"] = new Dictionary<string, object?>
            {
                ["@type"] = "SearchAction",
                ["target"] = new Dictionary<string, object?> { ["@type"] = "EntryPoint", ["urlTemplate"] = baseUrl + "/ara?q={search_term_string}" },
                ["query-input"] = "required name=search_term_string"
            }
        };
        return JsonSerializer.Serialize(new object[] { org, web }, JsonLdOpts);
    }

    /// <summary>Sayfalama için rel=prev/next (Google artık zayıf sinyal saysa da zararsız + diğer motorlar kullanır).</summary>
    private static string PageRel(string basePath, int page, int totalPages)
    {
        var sb = new StringBuilder();
        if (page > 1) sb.Append($"<link rel=\"prev\" href=\"{Enc(basePath)}{(page - 1 == 1 ? "" : $"?page={page - 1}")}\">");
        if (page < totalPages) sb.Append($"<link rel=\"next\" href=\"{Enc(basePath)}?page={page + 1}\">");
        return sb.ToString();
    }

    // ---------- Ana sayfa ----------
    public static string Home(SiteOptions o, IReadOnlyList<BlogListItem> posts, int page, int totalPages,
        IReadOnlyList<BlogListItem> topViewed, IReadOnlyList<(PublicCategory Cat, IReadOnlyList<BlogListItem> Posts)> catBlocks)
    {
        var head = SeoHead(o, page > 1 ? $"{o.SiteName} — Sayfa {page}" : o.SiteName, o.Description, page > 1 ? $"/blog?page={page}" : "/blog", null, "website", SiteJsonLd(o))
            + PageRel("/blog", page, totalPages);
        var b = new StringBuilder();
        b.Append(AdSlot(o, "Header", ""));

        if (posts.Count == 0)
        {
            b.Append($"<h1 class=\"serif\">{Enc(o.SiteName)}</h1><p class=\"dek\">{Enc(o.Description)}</p><p class=\"meta\">Henüz yazı yok.</p>");
            return Layout(o, head, b.ToString());
        }

        // ---- Hero (yalnız 1. sayfa) : slogan + daire görsel + öne çıkan kart ----
        // ÇALIŞAN SLIDER: ilk 3 yazı slayttır; oklar/noktalar gezdirir, 7 sn'de bir otomatik ilerler.
        if (page == 1)
        {
            var slides = posts.Take(3).ToList();
            var f = slides[0];
            b.Append("<section class=\"hero\"><div>");
            b.Append("<div class=\"eyebrow\">Her Konu, Her Masada.</div>");
            b.Append($"<h1 class=\"serif\">{Enc(o.HeroHeadline)}</h1>");
            b.Append($"<p class=\"sub\">{Enc(o.Description)}</p>");
            b.Append("<div class=\"btns\"><a class=\"btn pri\" id=\"hero-cta\" href=\"/blog/").Append(Enc(f.Slug)).Append("\">Bugünü Keşfet →</a><a class=\"btn ghost\" href=\"/yazilar\">Tüm Yazılar →</a></div>");
            b.Append("<div class=\"hero-nav\"><span class=\"ey\">Bugüne Dair</span><span class=\"dots\" id=\"hero-dots\">");
            for (var i = 0; i < slides.Count; i++)
                b.Append(i == 0 ? $"<b>0{i + 1}</b>" : $"<i></i>0{i + 1}");
            b.Append("</span>");
            if (slides.Count > 1)
                b.Append("<span class=\"harrows\"><button id=\"hero-prev\" aria-label=\"Önceki\">‹</button><button id=\"hero-next\" aria-label=\"Sonraki\">›</button></span>");
            b.Append("</div>");
            b.Append("</div>");
            // sağ: daire görsel + öne çıkan kart (id'ler slider için)
            b.Append("<div class=\"hero-media\"><span class=\"ring\"></span><a class=\"disc\" id=\"hero-disc\" href=\"/blog/").Append(Enc(f.Slug)).Append("\">");
            b.Append($"<img id=\"hero-img\" src=\"{Enc(SafeImg(f.CoverImageUrl) ?? "")}\" alt=\"{Enc(f.Title)}\"{(string.IsNullOrEmpty(SafeImg(f.CoverImageUrl)) ? " style=\"display:none\"" : "")}>");
            b.Append("</a>");
            b.Append($"<a class=\"feat-card\" id=\"hero-feat\" href=\"/blog/{Enc(f.Slug)}\"><div class=\"k\">Öne Çıkan</div><div class=\"t\" id=\"hero-title\">{Enc(f.Title)}</div>");
            b.Append($"<div class=\"m\"><span id=\"hero-date\">{Enc(o.SiteName)} · {f.PublishedAt.ToLocalTime():dd MMMM yyyy}</span><span class=\"go\">→</span></div></a>");
            b.Append("</div></section>");

            if (slides.Count > 1)
            {
                var slideJson = JsonSerializer.Serialize(slides.Select(p2 => new
                {
                    u = p2.Slug,
                    t = p2.Title,
                    c = SafeImg(p2.CoverImageUrl) ?? "",
                    m = $"{o.SiteName} · {p2.PublishedAt.ToLocalTime():dd MMMM yyyy}"
                }));
                b.Append("<script>(function(){var S=").Append(slideJson).Append(";var i=0,t;")
                 .Append("function pad(n){return (n<9?'0':'')+(n+1);}var $=function(id){return document.getElementById(id);};")
                 .Append("function render(){var s=S[i];$('hero-cta').href='/blog/'+encodeURIComponent(s.u);$('hero-disc').href='/blog/'+encodeURIComponent(s.u);$('hero-feat').href='/blog/'+encodeURIComponent(s.u);")
                 .Append("var im=$('hero-img');if(s.c){im.src=s.c;im.alt=s.t;im.style.display='block';}else{im.style.display='none';}")
                 .Append("$('hero-title').textContent=s.t;$('hero-date').textContent=s.m;")
                 .Append("var h='';for(var k=0;k<S.length;k++){h+=(k>0?'<i></i>':'')+(k===i?'<b>'+pad(k)+'</b>':pad(k));}$('hero-dots').innerHTML=h;}")
                 .Append("function go(d){i=(i+d+S.length)%S.length;render();restart();}")
                 .Append("function restart(){clearInterval(t);t=setInterval(function(){i=(i+1)%S.length;render();},7000);}")
                 .Append("$('hero-prev').onclick=function(e){e.preventDefault();go(-1);};$('hero-next').onclick=function(e){e.preventDefault();go(1);};restart();})();</script>");
            }
        }

        // ---- Sosyalde ... (markalı) ----
        b.Append(SocialStrip(o));

        // ---- Canlı akış (son yazılar, yatay) ----
        b.Append(LiveFeed(o, posts));

        // ---- Son yazılar ----
        var rest = (page == 1 ? posts.Skip(1) : posts).ToList();
        b.Append("<div class=\"sec-h\"><div><div class=\"ey\">Bugüne dair</div><h2 class=\"serif\">Son Yazılar</h2></div><a href=\"/yazilar\">Tümü →</a></div>");
        if (rest.Count > 0)
        {
            b.Append("<div class=\"grid3\">");
            b.Append(BigCard(rest[0]));
            foreach (var p in rest.Skip(1).Take(2)) b.Append(Card(p));
            b.Append("</div>");
            b.Append(AdSlot(o, "Homepage", ""));
            var more = rest.Skip(3).ToList();
            if (more.Count > 0)
            {
                b.Append("<div class=\"grid3\">");
                foreach (var p in more) b.Append(Card(p));
                b.Append("</div>");
            }
        }

        b.Append(Newsletter());
        // ---- En Çok Okunanlar (gerçek görüntülenme verisiyle) ----
        if (page == 1 && topViewed.Count > 0)
        {
            b.Append("<div class=\"sec-h\"><div><div class=\"ey\">Gündemin Nabzı</div><h2 class=\"serif\">En Çok Okunanlar</h2></div></div>");
            b.Append("<div class=\"mr\">");
            var no = 1;
            foreach (var p in topViewed.Take(6))
            {
                b.Append($"<a href=\"/blog/{Enc(p.Slug)}\"><span class=\"no\">{no:00}</span><span><span class=\"t\">{Enc(p.Title)}</span><div class=\"m\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy}</div></span></a>");
                no++;
            }
            b.Append("</div>");
        }

        // ---- Kategori vitrinleri (her kategoriden son 3 yazı) ----
        if (page == 1)
            foreach (var (cat, catPosts) in catBlocks)
            {
                if (catPosts.Count == 0) continue;
                var cUrl = $"/kategori/{Uri.EscapeDataString(cat.Slug)}";
                b.Append($"<div class=\"sec-h\"><div><div class=\"ey\">Kategori</div><h2 class=\"serif\">{Enc(cat.Name)}</h2></div><a href=\"{Enc(cUrl)}\">Tümü →</a></div>");
                b.Append("<div class=\"grid3\">");
                foreach (var p in catPosts.Take(3)) b.Append(Card(p));
                b.Append("</div>");
            }

        b.Append(Pager(page, totalPages, "/blog"));
        return Layout(o, head, b.ToString());
    }

    private static string CoverImg(string? url) =>
        string.IsNullOrEmpty(url) ? "" : $"<img src=\"{Enc(url)}\" alt=\"\" loading=\"lazy\">";

    // Kanal ikonu + zemin rengi (marka)
    private static (string Svg, string Bg) Brand(string name) => name switch
    {
        "Telegram" => (SvgTelegram, "#229ED9"),
        "X" => (SvgX, "#111"),
        "Instagram" => (SvgInstagram, "linear-gradient(45deg,#f9ce34,#ee2a7b,#6228d7)"),
        "Threads" => (SvgThreads, "#111"),
        "YouTube" => (SvgYoutube, "#FF0000"),
        "TikTok" => (SvgTikTok, "#010101"),
        _ => (SvgTelegram, "#229ED9")
    };

    // ---------- Canlı akış (son yazılardan) ----------
    private static string LiveFeed(SiteOptions o, IReadOnlyList<BlogListItem> posts)
    {
        var feed = posts.Take(6).ToList();
        if (feed.Count == 0) return "";
        // kanallar arasında döngü — görsel çeşitlilik (mockup'taki gibi)
        var brands = new[] { "X", "Instagram", "Threads", "Telegram", "YouTube" };
        var sb = new StringBuilder();
        sb.Append("<div class=\"live-h\"><span class=\"ey\">Canlı Akış</span><a href=\"/yazilar\" style=\"color:var(--accent);font-weight:600;font-size:14px\">Tümü →</a></div>");
        sb.Append("<div class=\"live\">");
        for (var i = 0; i < feed.Count; i++)
        {
            var p = feed[i];
            var name = brands[i % brands.Length];
            var (svg, bg) = Brand(name);
            sb.Append("<a class=\"lc\" href=\"/blog/").Append(Enc(p.Slug)).Append("\">");
            sb.Append($"<div class=\"h\"><span class=\"ic\" style=\"background:{bg}\">{svg}</span><span class=\"nm\">{name}</span><span class=\"tm\">{Ago(p.PublishedAt)}</span></div>");
            sb.Append($"<div class=\"tt\">{Enc(p.Title)}</div>");
            sb.Append(CoverDiv(p.CoverImageUrl, "im", p.Title));
            sb.Append($"<div class=\"st\"><span>{IconEye} {p.PublishedAt.ToLocalTime():dd MMM}</span></div>");
            sb.Append("</a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.UtcNow - t.ToUniversalTime();
        if (d.TotalMinutes < 60) return $"{Math.Max(1, (int)d.TotalMinutes)} dk önce";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} saat önce";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} gün önce";
        return t.ToLocalTime().ToString("dd MMM");
    }

    // ---------- Makale ----------
    public static string Post(SiteOptions o, BlogPostView p, PublicCategory? category, IReadOnlyList<BlogListItem> related, IReadOnlyList<CommentView> comments, bool submitted,
        IReadOnlyList<BlogListItem>? popular = null, BlogListItem? prevPost = null, BlogListItem? nextPost = null)
    {
        var path = $"/blog/{p.Slug}";
        var head = SeoHead(o, p.Title, p.MetaDescription, path, p.CoverImageUrl, "article", ArticleJsonLd(o, p, category)) + ArticleMeta(o, p, category);
        var (bodyHtml, toc) = BuildToc(NormalizeBody(p.BodyHtml));
        var adsOk = o.AdsEnabled && CountWords(p.BodyHtml) >= o.AdsMinWords;
        bodyHtml = InjectAd(bodyHtml, AdSlot(o, "InArticle1", "box", allow: adsOk), 3);
        var minutes = ReadingMinutes(p.BodyHtml);
        var kick = category?.Name ?? (p.Tags.Count > 0 ? p.Tags[0] : "Yazı");
        var shareUrl = Uri.EscapeDataString(Abs(o, path));
        var shareTitle = Uri.EscapeDataString(p.Title);

        var b = new StringBuilder();
        b.Append(AdSlot(o, "Header", ""));
        b.Append("<div class=\"crumbs\"><a href=\"/blog\">Ana Sayfa</a>");
        if (category is not null)
            b.Append($"<span>›</span><a href=\"/kategori/{Enc(Uri.EscapeDataString(category.Slug))}\">{Enc(category.Name)}</a>");
        else b.Append($"<span>›</span>{Enc(kick)}");
        b.Append($"<span>›</span>{Enc(p.Title)}</div>");
        b.Append("<div class=\"glayout\"><div class=\"article-col\"><div class=\"with-rail\">");

        // paylaşım rayı
        b.Append("<div class=\"share-rail\">");
        b.Append($"<a href=\"https://twitter.com/intent/tweet?url={shareUrl}&text={shareTitle}\" target=\"_blank\" rel=\"noopener\" title=\"X\">{SvgX}</a>");
        b.Append($"<a href=\"https://t.me/share/url?url={shareUrl}&text={shareTitle}\" target=\"_blank\" rel=\"noopener\" title=\"Telegram\">{SvgTelegram}</a>");
        b.Append($"<a href=\"https://api.whatsapp.com/send?text={shareTitle}%20{shareUrl}\" target=\"_blank\" rel=\"noopener\" title=\"WhatsApp\">{SvgWhatsApp}</a>");
        b.Append($"<a href=\"https://www.linkedin.com/sharing/share-offsite/?url={shareUrl}\" target=\"_blank\" rel=\"noopener\" title=\"LinkedIn\">{SvgLinkedIn}</a>");
        b.Append($"<a href=\"mailto:?subject={shareTitle}&body={shareUrl}\" title=\"E-posta\">{IconMail}</a>");
        b.Append($"<a href=\"#\" onclick=\"navigator.clipboard&&navigator.clipboard.writeText(location.href);this.title='Kopyalandı';return false;\" title=\"Bağlantıyı kopyala\">{IconLink}</a>");
        b.Append("</div>");

        b.Append("<article>");
        b.Append(category is not null
            ? $"<a class=\"kicker\" href=\"/kategori/{Enc(Uri.EscapeDataString(category.Slug))}\">{Enc(category.Name)}</a>"
            : $"<div class=\"kicker\">{Enc(kick)}</div>");
        b.Append($"<h1 class=\"title\">{Enc(p.Title)}</h1>");
        b.Append($"<p class=\"dek\">{Enc(p.MetaDescription)}</p>");
        b.Append("<div class=\"byline\">");
        b.Append($"<div class=\"avatar\">{Enc(o.Author.Substring(0, 1).ToUpperInvariant())}</div>");
        b.Append($"<div><div class=\"who\">{Enc(o.Author)} <span class=\"vf\" title=\"Doğrulanmış\">✔</span></div><div class=\"m\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy} · {minutes} dk okuma · {p.Views:N0} görüntülenme</div><div class=\"m\" style=\"font-size:12px;opacity:.85\">Yapay zekâ desteğiyle hazırlanıp editör onayından geçmiştir.</div></div>");
        b.Append("</div>");
        if (!string.IsNullOrEmpty(SafeImg(p.CoverImageUrl)))
            b.Append($"<img class=\"cover\" src=\"{Enc(p.CoverImageUrl!)}\" alt=\"{Enc(p.CoverImageAlt ?? p.Title)}\">");
        b.Append(AdSlot(o, "BelowIntroduction", "box", allow: adsOk));
        b.Append($"<div class=\"body\">{bodyHtml}</div>");
        b.Append(AdSlot(o, "AfterArticle", "", allow: adsOk));
        if (p.Tags.Count > 0)
        {
            b.Append("<div class=\"tags\">");
            foreach (var t in p.Tags) b.Append($"<a href=\"/etiket/{Enc(Uri.EscapeDataString(t))}\">#{Enc(t)}</a>");
            b.Append("</div>");
        }
        b.Append("<div class=\"share-inline\">");
        b.Append($"<a href=\"https://twitter.com/intent/tweet?url={shareUrl}&text={shareTitle}\" target=\"_blank\" rel=\"noopener\">𝕏 Paylaş</a>");
        b.Append($"<a href=\"https://t.me/share/url?url={shareUrl}&text={shareTitle}\" target=\"_blank\" rel=\"noopener\">✈ Telegram</a>");
        b.Append("</div>");
        b.Append("</article></div>"); // article + with-rail

        // ---- Önceki / Sonraki yazı (gezinme + iç linkleme) ----
        if (prevPost is not null || nextPost is not null)
        {
            b.Append("<nav class=\"pn\" aria-label=\"Yazı gezinme\">");
            b.Append(prevPost is not null
                ? $"<a href=\"/blog/{Enc(prevPost.Slug)}\"><div class=\"lbl\">← Önceki Yazı</div><div class=\"tt\">{Enc(prevPost.Title)}</div></a>"
                : "<span></span>");
            b.Append(nextPost is not null
                ? $"<a class=\"next\" href=\"/blog/{Enc(nextPost.Slug)}\"><div class=\"lbl\">Sonraki Yazı →</div><div class=\"tt\">{Enc(nextPost.Title)}</div></a>"
                : "<span></span>");
            b.Append("</nav>");
        }

        if (related.Count > 0)
        {
            b.Append("<section class=\"related\"><h2>İlgili yazılar</h2><div class=\"rgrid\">");
            foreach (var r in related.Take(3)) b.Append(RCard(r));
            b.Append("</div></section>");
        }
        b.Append(CommentsSection(p.Slug, comments, submitted));
        b.Append("</div>"); // article-col

        // sidebar
        b.Append("<aside class=\"side\">");
        if (toc.Length > 0) b.Append("<div class=\"panel toc\"><h3>Yazı İçeriği</h3>" + toc + "</div>");
        b.Append("<div class=\"panel news\"><h4>Bültenimize Katılın</h4><p>Haftanın öne çıkanları e-postana gelsin.</p><form method=\"post\" action=\"/blog/subscribe\" data-nl><input type=\"email\" name=\"email\" placeholder=\"E-posta adresiniz\" required><button type=\"submit\">Bültene Katıl</button></form><small>İstediğin zaman iptal edebilirsin.</small></div>");
        var popList = (popular is { Count: > 0 } ? popular : related);
        if (popList.Count > 0)
        {
            b.Append("<div class=\"panel\"><h3>Popüler Yazılar</h3><div style=\"padding:6px 8px 12px\">");
            foreach (var r in popList.Take(4))
                b.Append($"<a href=\"/blog/{Enc(r.Slug)}\" style=\"display:block;padding:10px;border-radius:10px\"><div style=\"font-weight:600;font-size:14.5px;line-height:1.35\">{Enc(r.Title)}</div><div style=\"color:var(--muted);font-size:12.5px;margin-top:3px\">{r.PublishedAt.ToLocalTime():dd MMMM yyyy}</div></a>");
            b.Append("</div></div>");
        }
        var rail = AdSlot(o, "Sidebar", "rail", allow: adsOk);
        if (rail.Length > 0) b.Append("<div class=\"rail-ad\">" + rail + "</div>");
        b.Append("</aside></div>"); // side + glayout

        return Layout(o, head, b.ToString());
    }

    public static string Tag(SiteOptions o, string tag, IReadOnlyList<BlogListItem> posts, int page = 1, int totalPages = 1)
    {
        var path = $"/etiket/{Uri.EscapeDataString(tag)}";
        var title = page > 1 ? $"#{tag} — Sayfa {page} — {o.SiteName}" : $"#{tag} — {o.SiteName}";
        var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "CollectionPage",
            ["name"] = $"#{tag}", ["url"] = Abs(o, path), ["inLanguage"] = "tr-TR"
        }, JsonLdOpts);
        var head = SeoHead(o, title, $"{tag} etiketli yazılar", page > 1 ? $"{path}?page={page}" : path, null, "website", jsonLd)
            + PageRel(path, page, totalPages);
        var b = new StringBuilder();
        b.Append($"<div class=\"sec-h\"><div><div class=\"ey\">Etiket</div><h2 class=\"serif\">#{Enc(tag)}</h2></div></div>");
        if (posts.Count == 0) b.Append("<p class=\"meta\">Bu etikette yazı yok.</p>");
        else { b.Append("<div class=\"grid3\">"); foreach (var p in posts) b.Append(Card(p)); b.Append("</div>"); }
        b.Append(Pager(page, totalPages, path));
        return Layout(o, head, b.ToString());
    }

    public static string NotFound(SiteOptions o)
    {
        var head = $"<title>Bulunamadı — {Enc(o.SiteName)}</title><meta name=\"robots\" content=\"noindex\">";
        var body = "<div style=\"max-width:560px;margin:40px auto;text-align:center\">"
            + "<h1 class=\"serif\" style=\"font-size:44px\">404</h1><p class=\"dek\">Aradığın sayfa bulunamadı ya da taşınmış olabilir.</p>"
            + "<form method=\"get\" action=\"/ara\" style=\"display:flex;gap:10px;margin:22px 0\">"
            + "<input type=\"search\" name=\"q\" placeholder=\"Sitede ara…\" required minlength=\"2\" style=\"flex:1;font:inherit;padding:12px 14px;border:1px solid var(--line);border-radius:12px;background:var(--bg);color:var(--fg)\">"
            + "<button type=\"submit\" style=\"font:inherit;font-weight:600;padding:12px 22px;border:none;border-radius:12px;background:var(--accent);color:#fff;cursor:pointer\">Ara</button></form>"
            + "<p><a class=\"btn pri\" href=\"/blog\">Ana sayfa →</a> <a class=\"btn ghost\" href=\"/yazilar\">Tüm yazılar →</a></p></div>";
        return Layout(o, head, body);
    }

    /// <summary>Hakkımızda (/hakkimizda) — E-E-A-T/güven sinyali. Metin panelden (site.about_text); boşsa otomatik.</summary>
    public static string About(SiteOptions o)
    {
        var head = SeoHead(o, $"Hakkımızda — {o.SiteName}",
            $"{o.SiteName} kimdir: yayın anlayışımız, içerik üretim sürecimiz ve bize ulaşma yolları.",
            "/hakkimizda", null, "website", null);
        var b = new StringBuilder();
        b.Append("<article class=\"legal\" style=\"max-width:820px;margin:0 auto\">");
        b.Append("<div class=\"kicker\">Kurumsal</div><h1 class=\"title\">Hakkımızda</h1><div class=\"body\">");
        if (!string.IsNullOrWhiteSpace(o.AboutText))
        {
            foreach (var para in o.AboutText!.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                b.Append($"<p>{Enc(para.Trim())}</p>");
        }
        else
        {
            b.Append($"<p><b>{Enc(o.SiteName)}</b>, {Enc(o.Description)}</p>");
            b.Append("<p>İçeriklerimiz güncel kaynaklardan derlenir, yapay zekâ desteğiyle hazırlanır ve yayına alınmadan önce editör onayından geçer. Amacımız; gündemi hızlı, anlaşılır ve tarafsız bir dille aktarmaktır.</p>");
            b.Append("<p>Haberlerimizde kaynaktaki bilgi ve atıflara sadık kalırız; içerik önerileriniz ve düzeltme talepleriniz bizim için değerlidir.</p>");
            b.Append($"<p>Bize ulaşmak için <a href=\"/iletisim\">İletişim</a> sayfamızı kullanabilir ya da <a href=\"mailto:{Enc(LegalEmail(o))}\">{Enc(LegalEmail(o))}</a> adresine yazabilirsiniz.</p>");
        }
        b.Append("</div></article>");
        return Layout(o, head, b.ToString());
    }

    /// <summary>İletişim (/iletisim) — e-posta + sosyal kanallar. AdSense/E-E-A-T için önemli.</summary>
    public static string Contact(SiteOptions o)
    {
        var head = SeoHead(o, $"İletişim — {o.SiteName}",
            $"{o.SiteName} ile iletişime geçin: e-posta ve sosyal medya kanallarımız.",
            "/iletisim", null, "website", null);
        var email = Enc(LegalEmail(o));
        var b = new StringBuilder();
        b.Append("<article class=\"legal\" style=\"max-width:820px;margin:0 auto\">");
        b.Append("<div class=\"kicker\">Kurumsal</div><h1 class=\"title\">İletişim</h1><div class=\"body\">");
        b.Append("<p>Soru, görüş, düzeltme talebi ve iş birlikleri için bize e-posta ile ulaşabilirsiniz:</p>");
        b.Append($"<p style=\"font-size:20px\"><a href=\"mailto:{email}\">{email}</a></p>");
        if (o.HomeSocials.Count > 0)
        {
            b.Append("<h2>Sosyal medya</h2><p>Bizi sosyal kanallarımızdan da takip edebilir, mesaj bırakabilirsiniz:</p><ul>");
            foreach (var sGroup in o.HomeSocials.GroupBy(x => x.Platform).OrderBy(g => g.Key))
                b.Append($"<li><a href=\"{Enc(sGroup.First().Url)}\" target=\"_blank\" rel=\"noopener\">{Enc(BrandName(sGroup.Key))}</a></li>");
            b.Append("</ul><p><a href=\"/sosyal\">Tüm sosyal kanallarımız →</a></p>");
        }
        b.Append($"<p style=\"color:var(--muted);font-size:14px\">Kişisel verilerinizle ilgili talepler için <a href=\"{Enc(string.IsNullOrWhiteSpace(o.PrivacyUrl) ? "/gizlilik-politikasi" : o.PrivacyUrl!)}\">Gizlilik Politikası</a> sayfamıza göz atın.</p>");
        b.Append("</div></article>");
        return Layout(o, head, b.ToString());
    }

    /// <summary>Site içi arama sayfası (/ara). Sonuç sayfaları noindex (SEO: arama sonuçları indekslenmez).</summary>
    public static string Search(SiteOptions o, string q, IReadOnlyList<BlogListItem> posts, int total, int page, int totalPages)
    {
        var head = $"<title>{Enc(q.Length > 0 ? $"\"{q}\" araması" : "Arama")} — {Enc(o.SiteName)}</title>"
            + "<meta name=\"robots\" content=\"noindex,follow\">"
            + $"<link rel=\"canonical\" href=\"{Enc(Abs(o, "/ara"))}\">";
        var b = new StringBuilder();
        b.Append("<div class=\"sec-h\"><div><div class=\"ey\">Arama</div><h2 class=\"serif\">Sitede Ara</h2></div></div>");
        b.Append("<form method=\"get\" action=\"/ara\" style=\"display:flex;gap:10px;margin:6px 0 26px;max-width:560px\">");
        b.Append($"<input type=\"search\" name=\"q\" value=\"{Enc(q)}\" placeholder=\"Haber, konu veya anahtar kelime…\" required minlength=\"2\" style=\"flex:1;font:inherit;padding:12px 14px;border:1px solid var(--line);border-radius:12px;background:var(--bg);color:var(--fg)\">");
        b.Append("<button type=\"submit\" style=\"font:inherit;font-weight:600;padding:12px 22px;border:none;border-radius:12px;background:var(--accent);color:#fff;cursor:pointer\">Ara</button></form>");

        if (q.Length >= 2)
        {
            b.Append($"<p class=\"meta\" style=\"margin:0 0 16px\">\"{Enc(q)}\" için {total} sonuç.</p>");
            if (posts.Count > 0)
            {
                b.Append("<div class=\"grid3\">");
                foreach (var p in posts) b.Append(Card(p));
                b.Append("</div>");
                // Sayfalayıcı (q korunur)
                if (totalPages > 1)
                {
                    var qe = Uri.EscapeDataString(q);
                    b.Append("<div class=\"btns\" style=\"justify-content:space-between;margin:24px 0\">");
                    b.Append(page > 1 ? $"<a class=\"btn ghost\" href=\"/ara?q={qe}&page={page - 1}\">← Önceki</a>" : "<span></span>");
                    b.Append(page < totalPages ? $"<a class=\"btn ghost\" href=\"/ara?q={qe}&page={page + 1}\">Sonraki →</a>" : "<span></span>");
                    b.Append("</div>");
                }
            }
            else b.Append("<p class=\"meta\">Sonuç bulunamadı — farklı bir ifade dene ya da <a href=\"/yazilar\" style=\"color:var(--accent)\">tüm yazılara</a> göz at.</p>");
        }
        return Layout(o, head, b.ToString());
    }

    /// <summary>Tüm yazılar arşivi (/yazilar) — sunucu tarafı sayfalı tam liste. "Tümü/Tüm Yazılar" linkleri buraya gelir.</summary>
    public static string AllPosts(SiteOptions o, IReadOnlyList<BlogListItem> posts, int page, int totalPages)
    {
        var title = page > 1 ? $"Tüm Yazılar — Sayfa {page} — {o.SiteName}" : $"Tüm Yazılar — {o.SiteName}";
        var jsonLd = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "CollectionPage",
            ["name"] = "Tüm Yazılar", ["url"] = Abs(o, "/yazilar"), ["inLanguage"] = "tr-TR"
        }, JsonLdOpts);
        var head = SeoHead(o, title, $"{o.SiteName} yazı arşivi: yayınlanan tüm haber ve yazılar.",
                page > 1 ? $"/yazilar?page={page}" : "/yazilar", null, "website", jsonLd)
            + PageRel("/yazilar", page, totalPages);
        var b = new StringBuilder();
        b.Append("<div class=\"sec-h\"><div><div class=\"ey\">Arşiv</div><h2 class=\"serif\">Tüm Yazılar</h2></div></div>");
        if (posts.Count == 0) b.Append("<p class=\"meta\">Henüz yazı yok.</p>");
        else
        {
            b.Append("<div class=\"grid3\">");
            foreach (var p in posts) b.Append(Card(p));
            b.Append("</div>");
        }
        b.Append(Pager(page, totalPages, "/yazilar"));
        return Layout(o, head, b.ToString());
    }

    // ---------- Yasal sayfalar (Gizlilik + Çerez) ----------
    private const string LegalUpdated = "18 Temmuz 2026";

    /// <summary>İletişim/KVKK e-postası: panelde girilmişse onu, yoksa alan adından türetir (iletisim@alanadi).</summary>
    private static string LegalEmail(SiteOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.ContactEmail)) return o.ContactEmail!.Trim();
        var host = Host(o);
        return host.Length > 0 ? $"iletisim@{host}" : "iletisim@" + o.SiteName;
    }

    public static string PrivacyPolicy(SiteOptions o)
    {
        var head = SeoHead(o, $"Gizlilik Politikası — {o.SiteName}",
            $"{o.SiteName} gizlilik politikası: hangi kişisel verileri topluyoruz, neden işliyoruz ve KVKK ile GDPR kapsamındaki haklarınız.",
            "/gizlilik-politikasi", null, "website", null);
        var site = Enc(o.SiteName);
        var email = Enc(LegalEmail(o));
        var cookieUrl = Enc(string.IsNullOrWhiteSpace(o.CookieUrl) ? "/cerez-politikasi" : o.CookieUrl!);
        var b = new StringBuilder();
        b.Append("<article class=\"legal\" style=\"max-width:820px;margin:0 auto\">");
        b.Append("<div class=\"kicker\">Yasal</div>");
        b.Append("<h1 class=\"title\">Gizlilik Politikası</h1>");
        b.Append($"<p class=\"dek\">{site} olarak gizliliğinize önem veriyoruz. Bu politika, sizi ziyaretiniz sırasında hangi kişisel verilerin işlendiği ve haklarınız konusunda bilgilendirir.</p>");
        b.Append("<div class=\"body\">");
        b.Append($"<p><b>Son güncelleme:</b> {LegalUpdated}</p>");

        b.Append("<h2>1. Veri Sorumlusu</h2>");
        b.Append($"<p>Bu web sitesi ({Enc(o.BaseUrlTrimmed)}) {site} tarafından işletilmektedir. 6698 sayılı Kişisel Verilerin Korunması Kanunu (KVKK) kapsamında veri sorumlusu {site}’dir. Kişisel verilerinizle ilgili her konuda <a href=\"mailto:{email}\">{email}</a> adresinden bize ulaşabilirsiniz.</p>");

        b.Append("<h2>2. Topladığımız Veriler</h2>");
        b.Append("<p>Sitemizi kullanırken aşağıdaki veriler işlenebilir:</p>");
        b.Append("<ul>");
        b.Append("<li><b>Otomatik toplanan veriler:</b> IP adresi, tarayıcı ve cihaz bilgisi, ziyaret edilen sayfalar, gelinen kaynak ve etkileşim istatistikleri (analitik çerezler aracılığıyla).</li>");
        b.Append("<li><b>Gönüllü verdiğiniz veriler:</b> Yorum bıraktığınızda adınız ve (isteğe bağlı) e-posta adresiniz; bültene abone olduğunuzda e-posta adresiniz.</li>");
        b.Append("</ul>");

        b.Append("<h2>3. Verileri Ne Amaçla İşliyoruz</h2>");
        b.Append("<ul>");
        b.Append("<li>Sitenin çalışması, güvenliği ve kötüye kullanımın önlenmesi,</li>");
        b.Append("<li>Ziyaretçi istatistikleriyle içerik ve deneyimin iyileştirilmesi,</li>");
        b.Append("<li>Yorumların yayınlanması ve moderasyonu,</li>");
        b.Append("<li>Talep etmeniz hâlinde bülten gönderimi,</li>");
        b.Append("<li>Yasal yükümlülüklerin yerine getirilmesi.</li>");
        b.Append("</ul>");

        b.Append("<h2>4. Çerezler ve Analitik</h2>");
        b.Append($"<p>Sitemiz, ziyaret istatistiklerini ölçmek için Google Analytics (GA4) kullanır ve bazı çerezler yerleştirir. Analitik ve reklam çerezleri yalnızca çerez onayı verdiğinizde etkinleştirilir (Google Consent Mode). Çerezler hakkında ayrıntı için <a href=\"{cookieUrl}\">Çerez Politikası</a> sayfamıza bakın.</p>");

        b.Append("<h2>5. Verilerin Paylaşımı ve Üçüncü Taraflar</h2>");
        b.Append("<p>Kişisel verilerinizi satmıyoruz. Veriler yalnızca hizmetin sağlanması için gerekli olduğunda ve ilgili hizmetin kendi gizlilik politikası kapsamında aşağıdaki üçüncü taraflarla işlenebilir:</p>");
        b.Append("<ul>");
        b.Append("<li><b>Google</b> (Analytics ve varsa AdSense) — ziyaret analizi ve reklam,</li>");
        b.Append("<li><b>Telegram</b> ve diğer sosyal platformlar — içerik paylaşımı ve etkileşim,</li>");
        b.Append("<li>Yasal olarak zorunlu hâllerde yetkili kamu kurumları.</li>");
        b.Append("</ul>");

        b.Append("<h2>6. Saklama Süresi</h2>");
        b.Append("<p>Verileriniz, işleme amacının gerektirdiği süre boyunca ve ilgili mevzuatta öngörülen süreler kadar saklanır; amaç ortadan kalktığında silinir veya anonim hâle getirilir.</p>");

        b.Append("<h2>7. Haklarınız</h2>");
        b.Append("<p>KVKK m. 11 ve GDPR kapsamında; verilerinize erişme, düzeltilmesini veya silinmesini isteme, işlemeye itiraz etme ve verilerinizin taşınmasını talep etme haklarına sahipsiniz. Bu haklarınızı kullanmak için <a href=\"mailto:" + email + "\">" + email + "</a> adresine yazabilirsiniz.</p>");

        b.Append("<h2>8. Güvenlik</h2>");
        b.Append("<p>Verilerinizi yetkisiz erişime karşı korumak için makul teknik ve idari tedbirleri alıyoruz. Bununla birlikte internet üzerinden hiçbir aktarımın %100 güvenli olmadığını hatırlatırız.</p>");

        b.Append("<h2>9. Değişiklikler</h2>");
        b.Append("<p>Bu politika zaman zaman güncellenebilir. Güncel sürüm her zaman bu sayfada yayınlanır; önemli değişikliklerde güncelleme tarihi yenilenir.</p>");

        b.Append("<h2>10. İletişim</h2>");
        b.Append($"<p>Sorularınız için: <a href=\"mailto:{email}\">{email}</a></p>");

        b.Append("</div></article>");
        return Layout(o, head, b.ToString());
    }

    /// <summary>Kullanım Şartları — TikTok/Meta gibi platform başvuruları "Terms of Service URL" ister; ayrıca iyi pratik.</summary>
    public static string TermsOfService(SiteOptions o)
    {
        var head = SeoHead(o, $"Kullanım Şartları — {o.SiteName}",
            $"{o.SiteName} kullanım şartları: siteyi kullanırken geçerli koşullar, fikri mülkiyet, sorumluluk sınırları ve iletişim.",
            "/kullanim-sartlari", null, "website", null);
        var site = Enc(o.SiteName);
        var email = Enc(LegalEmail(o));
        var b = new StringBuilder();
        b.Append("<article class=\"legal\" style=\"max-width:820px;margin:0 auto\">");
        b.Append("<div class=\"kicker\">Yasal</div>");
        b.Append("<h1 class=\"title\">Kullanım Şartları</h1>");
        b.Append($"<p class=\"dek\">Bu sayfa, {site} web sitesini ve bağlı sosyal medya kanallarını kullanırken geçerli olan koşulları açıklar. Siteyi kullanarak bu şartları kabul etmiş sayılırsınız.</p>");
        b.Append("<div class=\"body\">");
        b.Append($"<p><b>Son güncelleme:</b> {LegalUpdated}</p>");

        b.Append("<h2>1. Hizmetin Kapsamı</h2>");
        b.Append($"<p>{site} ({Enc(o.BaseUrlTrimmed)}); gündem, teknoloji, ekonomi ve ilgili alanlarda haber ve analiz içerikleri yayımlayan bir dijital yayın platformudur. İçerikler bilgilendirme amaçlıdır; yatırım, hukuk veya başka bir alanda profesyonel danışmanlık yerine geçmez.</p>");

        b.Append("<h2>2. Fikri Mülkiyet</h2>");
        b.Append($"<p>Sitedeki yazılar, görseller, videolar ve tasarım öğeleri aksi belirtilmedikçe {site}’e aittir ya da lisanslıdır. Kaynak gösterilerek kısa alıntı yapılabilir; içeriklerin izinsiz kopyalanması, çoğaltılması veya ticari kullanımı yasaktır.</p>");

        b.Append("<h2>3. Kullanıcı İçerikleri</h2>");
        b.Append("<p>Yorum ve benzeri alanlarda paylaştığınız içeriklerden siz sorumlusunuz. Hakaret, nefret söylemi, yanıltıcı bilgi ve hukuka aykırı içerikler yayından kaldırılabilir.</p>");

        b.Append("<h2>4. Sorumluluk Sınırı</h2>");
        b.Append($"<p>{site}, içeriklerin güncelliği ve doğruluğu için özen gösterir; ancak içeriklerin kullanımından doğabilecek doğrudan ya da dolaylı zararlardan sorumlu tutulamaz. Dış bağlantıların içeriği ilgili sitelerin sorumluluğundadır.</p>");

        b.Append("<h2>5. Değişiklikler</h2>");
        b.Append("<p>Bu şartlar gerektiğinde güncellenebilir; güncel sürüm her zaman bu sayfada yayımlanır.</p>");

        b.Append("<h2>6. İletişim</h2>");
        b.Append($"<p>Sorularınız için: <a href=\"mailto:{email}\">{email}</a></p>");
        b.Append("</div></article>");
        return Layout(o, head, b.ToString());
    }

    public static string CookiePolicy(SiteOptions o)
    {
        var head = SeoHead(o, $"Çerez Politikası — {o.SiteName}",
            $"{o.SiteName} çerez politikası: hangi çerezleri neden kullanıyoruz ve çerez tercihlerinizi nasıl yönetebilirsiniz.",
            "/cerez-politikasi", null, "website", null);
        var site = Enc(o.SiteName);
        var email = Enc(LegalEmail(o));
        var privUrl = Enc(string.IsNullOrWhiteSpace(o.PrivacyUrl) ? "/gizlilik-politikasi" : o.PrivacyUrl!);
        var b = new StringBuilder();
        b.Append("<article class=\"legal\" style=\"max-width:820px;margin:0 auto\">");
        b.Append("<div class=\"kicker\">Yasal</div>");
        b.Append("<h1 class=\"title\">Çerez Politikası</h1>");
        b.Append($"<p class=\"dek\">{site} olarak sitemizde çerezler kullanıyoruz. Bu sayfa hangi çerezleri neden kullandığımızı ve tercihlerinizi nasıl yöneteceğinizi açıklar.</p>");
        b.Append("<div class=\"body\">");
        b.Append($"<p><b>Son güncelleme:</b> {LegalUpdated}</p>");

        b.Append("<h2>1. Çerez Nedir?</h2>");
        b.Append("<p>Çerezler, ziyaret ettiğiniz siteler tarafından tarayıcınıza kaydedilen küçük metin dosyalarıdır. Sitenin düzgün çalışmasına, tercihlerinizin hatırlanmasına ve ziyaretlerin ölçülmesine yardımcı olurlar.</p>");

        b.Append("<h2>2. Kullandığımız Çerez Türleri</h2>");
        b.Append("<table style=\"width:100%;border-collapse:collapse;margin:8px 0\">");
        b.Append("<thead><tr><th style=\"text-align:left;border-bottom:1px solid var(--line);padding:8px\">Tür</th><th style=\"text-align:left;border-bottom:1px solid var(--line);padding:8px\">Amaç</th><th style=\"text-align:left;border-bottom:1px solid var(--line);padding:8px\">Onay</th></tr></thead><tbody>");
        b.Append("<tr><td style=\"padding:8px;vertical-align:top\"><b>Zorunlu</b></td><td style=\"padding:8px\">Sitenin çalışması ve çerez tercihinizin hatırlanması (ör. <code>cc_consent</code>). Bu çerezler olmadan site düzgün çalışmaz.</td><td style=\"padding:8px\">Gerekmez</td></tr>");
        b.Append("<tr><td style=\"padding:8px;vertical-align:top\"><b>Analitik</b></td><td style=\"padding:8px\">Ziyaretçi sayısı ve davranışını anonim ölçmek için Google Analytics (ör. <code>_ga</code>, <code>_ga_*</code>).</td><td style=\"padding:8px\">Onayınıza bağlı</td></tr>");
        b.Append("<tr><td style=\"padding:8px;vertical-align:top\"><b>Reklam</b></td><td style=\"padding:8px\">Reklam gösterimi etkinse (ör. Google AdSense) reklamları ölçmek ve kişiselleştirmek için.</td><td style=\"padding:8px\">Onayınıza bağlı</td></tr>");
        b.Append("</tbody></table>");

        b.Append("<h2>3. Onay ve Google Consent Mode</h2>");
        b.Append("<p>Analitik ve reklam çerezleri, sitedeki çerez bandından <b>Kabul et</b>’e tıklamadığınız sürece <b>çalışmaz</b>. Tercihinizi Google Consent Mode ile uyguluyoruz; reddederseniz bu çerezler devre dışı kalır.</p>");

        b.Append("<h2>4. Çerezleri Nasıl Yönetirsiniz?</h2>");
        b.Append("<ul>");
        b.Append("<li>Sitedeki çerez bandından tercihinizi belirleyebilir; kararınızı değiştirmek için tarayıcınızın <code>cc_consent</code> çerezini silip sayfayı yenileyebilirsiniz.</li>");
        b.Append("<li>Tarayıcı ayarlarından tüm çerezleri engelleyebilir veya silebilirsiniz. Zorunlu çerezleri engellemek sitenin bazı bölümlerini etkileyebilir.</li>");
        b.Append("</ul>");

        b.Append("<h2>5. Daha Fazla Bilgi</h2>");
        b.Append($"<p>Kişisel verilerinizin işlenmesiyle ilgili ayrıntılar için <a href=\"{privUrl}\">Gizlilik Politikası</a> sayfamıza bakabilir, sorularınız için <a href=\"mailto:{email}\">{email}</a> adresine yazabilirsiniz.</p>");

        b.Append("</div></article>");
        return Layout(o, head, b.ToString());
    }

    // ---------- Parçalar ----------
    private static string Kicker(IReadOnlyList<string> tags, string prefix) =>
        tags.Count > 0 ? prefix + Enc(tags[0]) : "";

    /// <summary>Video (.mp4) URL'i GÖRSEL değildir — eski kayıtlarda kapağa sızmışsa boş kapak bas (kırık ikon yerine).</summary>
    private static string? SafeImg(string? url) =>
        url is not null && url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? null : url;

    private static string CoverDiv(string? url, string cls, string alt = "") =>
        string.IsNullOrEmpty(url = SafeImg(url))
            ? $"<div class=\"{cls}\"></div>"
            : $"<img class=\"{cls}\" src=\"{Enc(url)}\" alt=\"{Enc(alt)}\" loading=\"lazy\">";

    private static string Card(BlogListItem p)
    {
        var sb = new StringBuilder("<a class=\"card\" href=\"/blog/").Append(Enc(p.Slug)).Append("\">");
        sb.Append(CoverDiv(p.CoverImageUrl, "im", p.Title));
        sb.Append("<div class=\"p\">");
        if (p.Tags.Count > 0) sb.Append($"<div class=\"k\">{Enc(p.Tags[0])}</div>");
        sb.Append($"<h3 class=\"t\">{Enc(p.Title)}</h3>");
        sb.Append($"<div class=\"m\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy}</div>");
        sb.Append("</div></a>");
        return sb.ToString();
    }

    private static string BigCard(BlogListItem p)
    {
        var sb = new StringBuilder("<a class=\"card big\" href=\"/blog/").Append(Enc(p.Slug)).Append("\">");
        sb.Append(CoverDiv(p.CoverImageUrl, "im", p.Title));
        sb.Append("<div class=\"p\">");
        if (p.Tags.Count > 0) sb.Append($"<div class=\"k\">{Enc(p.Tags[0])}</div>");
        sb.Append($"<h3 class=\"t\">{Enc(p.Title)}</h3>");
        sb.Append($"<p class=\"d\">{Enc(p.MetaDescription)}</p>");
        sb.Append($"<div class=\"m\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy}</div>");
        sb.Append("</div></a>");
        return sb.ToString();
    }

    private static string RCard(BlogListItem p)
    {
        var sb = new StringBuilder("<a class=\"rcard\" href=\"/blog/").Append(Enc(p.Slug)).Append("\">");
        sb.Append(CoverDiv(p.CoverImageUrl, "im", p.Title));
        sb.Append("<div class=\"p\">");
        if (p.Tags.Count > 0) sb.Append($"<div class=\"k\">{Enc(p.Tags[0])}</div>");
        sb.Append($"<div class=\"t\">{Enc(p.Title)}</div>");
        sb.Append("</div></a>");
        return sb.ToString();
    }

    private static string Newsletter() =>
        "<section class=\"nl\"><h3 class=\"serif\">Bültenimize Katılın</h3><p>Haftanın öne çıkan gelişmelerini kaçırma, doğrudan e-postana gelsin.</p>"
        + "<form method=\"post\" action=\"/blog/subscribe\" data-nl><input type=\"email\" name=\"email\" placeholder=\"E-posta adresiniz\" required><button type=\"submit\">Bültene Katıl</button></form></section>";

    /// <summary>
    /// Ana sayfa "Sosyalde ..." şeridi. ÖNCELİK: "Sosyal Hesaplar"da 'ana sayfada yayınla' seçili kanallar
    /// (o.HomeSocials). Bir platformda birden çok kanal seçilebilir; hepsi ayrı kart olur. Hiç seçim yoksa
    /// yedek Site/SEO alanlarına (TelegramUrl/XUrl...) düşer — böylece geçiş döneminde ana sayfa boş kalmaz.
    /// </summary>
    private static string SocialStrip(SiteOptions o)
    {
        var items = new List<string>();
        string? firstUrl = null;

        void Add(string? url, string? count, string name, string handle, string label)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            firstUrl ??= url;
            var (svg, bg) = Brand(name);
            var btnLabel = name == "Telegram" ? "Katıl" : (name == "YouTube" ? "Abone Ol" : "Takip Et");
            var sb = new StringBuilder("<div class=\"ch\">");
            sb.Append($"<div class=\"top\"><span class=\"ic\" style=\"background:{bg}\">{svg}</span><div><div class=\"nm\">{Enc(name)}</div><div class=\"hd\">{Enc(handle)}</div></div></div>");
            if (!string.IsNullOrWhiteSpace(count)) sb.Append($"<div class=\"n\">{Enc(count)}</div>");
            sb.Append($"<div class=\"lbl\">{Enc(label)}</div>");
            sb.Append($"<a class=\"f\" style=\"background:{bg}\" href=\"{Enc(url)}\" target=\"_blank\" rel=\"noopener\">{btnLabel}</a></div>");
            items.Add(sb.ToString());
        }

        // YALNIZ "Sosyal Hesaplar"da 'ana sayfada yayınla' seçili kanallar (ayarlardan GELMEZ).
        // Yüzlerce kanal olabilir → her sayfa açılışında RASTGELE 5'i gösterilir; tamamı /sosyal sayfasında.
        if (o.HomeSocials.Count == 0) return "";
        var pick = o.HomeSocials.ToList();
        for (var i = pick.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (pick[i], pick[j]) = (pick[j], pick[i]);
        }
        foreach (var s in pick.Take(5))
        {
            var name = BrandName(s.Platform);
            var label = name == "Telegram" ? "Üye" : (name == "YouTube" ? "Abone" : "Takipçi");
            var handle = HandleFromUrl(s.Url) ?? (string.IsNullOrWhiteSpace(s.Title) ? name : s.Title);
            var count = s.Followers is { } f and > 0 ? FormatCount(f) : null;
            Add(s.Url, count, name, handle, label);
        }
        _ = firstUrl; // "Tümü" artık dâhilî /sosyal sayfasına gider

        if (items.Count == 0) return "";
        return "<section class=\"social\"><div class=\"lead\"><div class=\"ey\">Sosyalde " + Enc(o.SiteName) + "</div><h3 class=\"serif\">Bize katıl</h3><p>Anında paylaş, haberdar ol, topluluğumuza katıl.</p><a class=\"all\" href=\"/sosyal\">Tüm Sosyal Kanallar →</a></div><div class=\"chan\">"
            + string.Concat(items) + "</div></section>";
    }

    /// <summary>Tüm sosyal kanallar sayfası (/sosyal) — 'ana sayfada yayınla' seçili TÜM kanallar, platforma göre gruplu.</summary>
    public static string AllSocials(SiteOptions o)
    {
        var head = SeoHead(o, $"Sosyal Kanallarımız — {o.SiteName}",
            $"{o.SiteName} resmi sosyal medya kanalları: Telegram, X, Instagram, Threads, YouTube, TikTok.",
            "/sosyal", null, "website", null);
        var b = new StringBuilder();
        b.Append("<div class=\"sec-h\"><div><div class=\"ey\">Topluluk</div><h2 class=\"serif\">Sosyal Kanallarımız</h2></div></div>");
        if (o.HomeSocials.Count == 0)
        {
            b.Append("<p class=\"meta\">Henüz kanal eklenmedi.</p>");
            return Layout(o, head, b.ToString());
        }
        foreach (var group in o.HomeSocials.GroupBy(s => s.Platform).OrderBy(g => g.Key))
        {
            var name = BrandName(group.Key);
            b.Append($"<h3 class=\"serif\" style=\"margin:26px 0 12px\">{Enc(name)}</h3><div class=\"chan\">");
            foreach (var s in group)
            {
                var (svg, bg) = Brand(name);
                var label = name == "Telegram" ? "Üye" : (name == "YouTube" ? "Abone" : "Takipçi");
                var btnLabel = name == "Telegram" ? "Katıl" : (name == "YouTube" ? "Abone Ol" : "Takip Et");
                var handle = HandleFromUrl(s.Url) ?? s.Title;
                b.Append("<div class=\"ch\">");
                b.Append($"<div class=\"top\"><span class=\"ic\" style=\"background:{bg}\">{svg}</span><div><div class=\"nm\">{Enc(string.IsNullOrWhiteSpace(s.Title) ? name : s.Title)}</div><div class=\"hd\">{Enc(handle)}</div></div></div>");
                if (s.Followers is { } f and > 0) b.Append($"<div class=\"n\">{Enc(FormatCount(f))}</div>");
                b.Append($"<div class=\"lbl\">{Enc(label)}</div>");
                b.Append($"<a class=\"f\" style=\"background:{bg}\" href=\"{Enc(s.Url)}\" target=\"_blank\" rel=\"noopener\">{btnLabel}</a></div>");
            }
            b.Append("</div>");
        }
        return Layout(o, head, b.ToString());
    }

    /// <summary>Platform enum → ana sayfa marka adı (ikon + renk Brand() ile eşleşir).</summary>
    private static string BrandName(Platform p) => p switch
    {
        Platform.Telegram => "Telegram",
        Platform.X => "X",
        Platform.Instagram => "Instagram",
        Platform.Threads => "Threads",
        Platform.Youtube => "YouTube",
        Platform.TikTok => "TikTok",
        _ => "Telegram"
    };

    /// <summary>Herkese açık URL'den okunur bir @handle çıkarır (son yol parçası). Bulunamazsa null.</summary>
    private static string? HandleFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var seg = u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var last = seg.Length > 0 ? seg[^1] : u.Host;
        last = last.TrimStart('@');
        return string.IsNullOrWhiteSpace(last) ? null : "@" + last;
    }

    /// <summary>Takipçi sayısını kısaltır: 12500 → "12,5B", 1200000 → "1,2M".</summary>
    private static string FormatCount(int n)
    {
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        if (n >= 1_000_000) return (n / 1_000_000d).ToString("0.#", tr) + "M";
        if (n >= 1_000) return (n / 1_000d).ToString("0.#", tr) + "B";
        return n.ToString("N0", tr);
    }

    private static string Host(SiteOptions o) =>
        Uri.TryCreate(o.BaseUrlTrimmed, UriKind.Absolute, out var u) ? u.Host : "";

    private static string CommentsSection(string slug, IReadOnlyList<CommentView> comments, bool submitted)
    {
        var sb = new StringBuilder();
        sb.Append($"<section id=\"comments\" class=\"comments\"><h2>Yorumlar ({comments.Count})</h2>");
        sb.Append("<p style=\"color:var(--muted);font-size:14px;margin:0\">Yorumlar moderasyondan sonra yayınlanır.</p>");
        if (submitted) sb.Append("<p class=\"note\">Yorumun alındı; onaylandıktan sonra görünecek.</p>");
        foreach (var c in comments)
            sb.Append($"<div class=\"cmt\"><div class=\"av\">{Enc(c.AuthorName.Length > 0 ? c.AuthorName.Substring(0, 1).ToUpperInvariant() : "?")}</div><div><div><span class=\"who\">{Enc(c.AuthorName)}</span> <span class=\"when\">· {c.CreatedAt.ToLocalTime():dd MMMM yyyy}</span></div><p>{Enc(c.Body)}</p></div></div>");
        if (comments.Count == 0) sb.Append("<p style=\"color:var(--muted)\">İlk yorumu sen yaz.</p>");
        sb.Append($"<form class=\"cmt-form\" method=\"post\" action=\"/blog/{Enc(Uri.EscapeDataString(slug))}/comment\">");
        sb.Append("<div class=\"row\"><input name=\"name\" maxlength=\"80\" placeholder=\"Adın\" required><input name=\"email\" type=\"email\" maxlength=\"200\" placeholder=\"E-posta (yayınlanmaz)\"></div>");
        sb.Append("<textarea name=\"body\" maxlength=\"4000\" placeholder=\"Yorumun…\" required></textarea>");
        sb.Append("<button type=\"submit\">Gönder</button></form></section>");
        return sb.ToString();
    }

    private static string Pager(int page, int totalPages, string basePath)
    {
        if (totalPages <= 1) return "";
        var b = new StringBuilder("<div class=\"btns\" style=\"justify-content:space-between;margin:24px 0\">");
        b.Append(page > 1 ? $"<a class=\"btn ghost\" href=\"{basePath}?page={page - 1}\">← Önceki</a>" : "<span></span>");
        b.Append(page < totalPages ? $"<a class=\"btn ghost\" href=\"{basePath}?page={page + 1}\">Sonraki →</a>" : "<span></span>");
        b.Append("</div>");
        return b.ToString();
    }

    /// <summary>
    /// Gövde DÜZ METİN ise (blok HTML etiketi yoksa) editördeki düzeni HTML'e çevirir:
    /// boş satır → paragraf, tek satır sonu → &lt;br&gt;, satır başındaki "## " → h2, "### " → h3.
    /// Böylece yayın, editörde görünenle AYNI paragraf/başlık düzeninde çıkar. Zaten HTML ise dokunmaz.
    /// </summary>
    internal static string NormalizeBody(string? raw)
    {
        var text = raw ?? "";
        if (Regex.IsMatch(text, @"<\s*(p|h[1-6]|ul|ol|li|div|table|blockquote|pre|br)\b", RegexOptions.IgnoreCase))
            return text; // zaten blok HTML — editör böyle üretmiş, aynen bas

        var sb = new StringBuilder();
        var blocks = Regex.Split(text.Replace("\r\n", "\n").Trim(), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var b = block.Trim();
            if (b.Length == 0) continue;
            if (b.StartsWith("### ", StringComparison.Ordinal))
                sb.Append("<h3>").Append(b[4..].Trim()).Append("</h3>");
            else if (b.StartsWith("## ", StringComparison.Ordinal))
                sb.Append("<h2>").Append(b[3..].Trim()).Append("</h2>");
            else if (b.StartsWith("# ", StringComparison.Ordinal))
                sb.Append("<h2>").Append(b[2..].Trim()).Append("</h2>");
            else
                sb.Append("<p>").Append(b.Replace("\n", "<br>")).Append("</p>");
        }
        return sb.Length > 0 ? sb.ToString() : text;
    }

    // gövdedeki <h2>/<h3>'lere id ekler + TOC listesi üretir
    private static (string body, string toc) BuildToc(string html)
    {
        var items = new List<(int level, string id, string title)>();
        var i = 0;
        var body = Regex.Replace(html, "<(h2|h3)>(.*?)</\\1>", m =>
        {
            var tag = m.Groups[1].Value;
            var inner = m.Groups[2].Value;
            var title = Regex.Replace(inner, "<[^>]+>", "").Trim();
            if (title.Length == 0) return m.Value;
            var id = "b" + (++i);
            items.Add((tag == "h2" ? 2 : 3, id, title));
            return $"<{tag} id=\"{id}\">{inner}</{tag}>";
        }, RegexOptions.Singleline);

        if (items.Count == 0) return (body, "");
        var toc = new StringBuilder("<ol id=\"toc\">");
        foreach (var it in items)
            toc.Append(it.level == 3 ? "<li class=\"sub\">" : "<li>")
               .Append($"<a href=\"#{it.id}\">{Enc(it.title)}</a></li>");
        toc.Append("</ol>");
        return (body, toc.ToString());
    }

    // gövde içine, n. </p>'den sonra reklam yerleştirir
    private static string InjectAd(string body, string ad, int afterParagraph)
    {
        if (string.IsNullOrEmpty(ad)) return body;
        var idx = -1;
        for (var k = 0; k < afterParagraph; k++)
        {
            idx = body.IndexOf("</p>", idx + 1, StringComparison.Ordinal);
            if (idx < 0) return body;
        }
        idx += 4;
        return body.Substring(0, idx) + ad + body.Substring(idx);
    }

    private static int CountWords(string html)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        return Regex.Matches(text, "\\S+").Count;
    }

    private static int ReadingMinutes(string html) => Math.Max(1, (int)Math.Round(CountWords(html) / 200.0));

    /// <summary>Makale için <meta property="article:*"> etiketleri (OG article + section + tags).</summary>
    private static string ArticleMeta(SiteOptions o, BlogPostView p, PublicCategory? cat)
    {
        var sb = new StringBuilder();
        sb.Append($"<meta property=\"article:published_time\" content=\"{p.PublishedAt:o}\">");
        sb.Append($"<meta property=\"article:modified_time\" content=\"{(p.UpdatedAt ?? p.PublishedAt):o}\">");
        sb.Append($"<meta property=\"article:author\" content=\"{Enc(o.Author)}\">");
        if (cat is not null) sb.Append($"<meta property=\"article:section\" content=\"{Enc(cat.Name)}\">");
        foreach (var t in p.Tags.Where(t => !string.IsNullOrWhiteSpace(t) && !t.StartsWith('_')).Take(8))
            sb.Append($"<meta property=\"article:tag\" content=\"{Enc(t)}\">");
        return sb.ToString();
    }

    private static string ArticleJsonLd(SiteOptions o, BlogPostView p, PublicCategory? cat)
    {
        var url = Abs(o, $"/blog/{p.Slug}");
        var publisher = new Dictionary<string, object?>
        {
            ["@type"] = "Organization", ["name"] = o.SiteName, ["url"] = o.BaseUrlTrimmed,
            ["logo"] = new Dictionary<string, object?> { ["@type"] = "ImageObject", ["url"] = Abs(o, "/logo.svg") }
        };
        var article = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "NewsArticle",
            ["headline"] = p.Title.Length > 110 ? p.Title[..110] : p.Title,
            ["description"] = p.MetaDescription,
            ["datePublished"] = p.PublishedAt.ToString("o"),
            ["dateModified"] = (p.UpdatedAt ?? p.PublishedAt).ToString("o"),
            ["mainEntityOfPage"] = new Dictionary<string, object?> { ["@type"] = "WebPage", ["@id"] = url },
            ["url"] = url,
            ["inLanguage"] = "tr-TR",
            ["isAccessibleForFree"] = true,
            ["wordCount"] = CountWords(p.BodyHtml),
            ["author"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = o.Author, ["url"] = o.BaseUrlTrimmed },
            ["publisher"] = publisher
        };
        if (cat is not null) article["articleSection"] = cat.Name;
        var keywords = p.Tags.Where(t => !string.IsNullOrWhiteSpace(t) && !t.StartsWith('_')).ToList();
        if (keywords.Count > 0) article["keywords"] = string.Join(", ", keywords);
        if (!string.IsNullOrEmpty(p.CoverImageUrl))
            article["image"] = new[] { Abs(o, p.CoverImageUrl!) };

        // Breadcrumb: Ana Sayfa → (Kategori) → Yazı
        var items = new List<object> { new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Ana Sayfa", ["item"] = Abs(o, "/blog") } };
        var pos = 2;
        if (cat is not null)
            items.Add(new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = pos++, ["name"] = cat.Name, ["item"] = Abs(o, $"/kategori/{Uri.EscapeDataString(cat.Slug)}") });
        items.Add(new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = pos, ["name"] = p.Title, ["item"] = url });
        var breadcrumb = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList", ["itemListElement"] = items
        };
        return JsonSerializer.Serialize(new object[] { article, breadcrumb }, JsonLdOpts);
    }

    // ---------- Kategori sayfası (SEO iniş sayfası) ----------
    public static string Category(SiteOptions o, PublicCategory cat, IReadOnlyList<BlogListItem> posts, int page, int totalPages)
    {
        var path = $"/kategori/{Uri.EscapeDataString(cat.Slug)}";
        var title = page > 1 ? $"{cat.Name} — Sayfa {page} — {o.SiteName}" : $"{cat.Name} — {o.SiteName}";
        var desc = $"{cat.Name} kategorisindeki güncel yazılar, haberler ve analizler — {o.SiteName}.";
        var head = SeoHead(o, title, desc, page > 1 ? $"{path}?page={page}" : path, null, "website", CategoryJsonLd(o, cat, posts))
            + PageRel(path, page, totalPages);
        var b = new StringBuilder();
        b.Append(AdSlot(o, "Header", ""));
        b.Append($"<div class=\"crumbs\"><a href=\"/blog\">Ana Sayfa</a><span>›</span>{Enc(cat.Name)}</div>");
        b.Append($"<div class=\"sec-h\"><div><div class=\"ey\">Kategori</div><h1 class=\"serif\" style=\"font-size:32px;margin:0\">{Enc(cat.Name)}</h1></div><a href=\"/blog\">Tüm yazılar →</a></div>");
        if (posts.Count == 0)
            b.Append("<p class=\"empty\">Bu kategoride henüz yazı yok.</p>");
        else
        {
            b.Append("<div class=\"grid3\">");
            b.Append(BigCard(posts[0]));
            foreach (var pp in posts.Skip(1).Take(2)) b.Append(Card(pp));
            foreach (var pp in posts.Skip(3)) b.Append(Card(pp));
            b.Append("</div>");
        }
        b.Append(AdSlot(o, "CategoryPage", ""));
        b.Append(Pager(page, totalPages, path));
        return Layout(o, head, b.ToString());
    }

    private static string CategoryJsonLd(SiteOptions o, PublicCategory cat, IReadOnlyList<BlogListItem> posts)
    {
        var url = Abs(o, $"/kategori/{Uri.EscapeDataString(cat.Slug)}");
        var itemList = new Dictionary<string, object?>
        {
            ["@type"] = "ItemList",
            ["itemListElement"] = posts.Take(10).Select((pp, i) => (object)new Dictionary<string, object?>
            {
                ["@type"] = "ListItem", ["position"] = i + 1, ["url"] = Abs(o, $"/blog/{pp.Slug}"), ["name"] = pp.Title
            }).ToList()
        };
        var page = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "CollectionPage",
            ["name"] = cat.Name, ["url"] = url, ["inLanguage"] = "tr-TR", ["mainEntity"] = itemList
        };
        var breadcrumb = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList",
            ["itemListElement"] = new object[]
            {
                new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Ana Sayfa", ["item"] = Abs(o, "/blog") },
                new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = cat.Name, ["item"] = url }
            }
        };
        return JsonSerializer.Serialize(new object[] { page, breadcrumb }, JsonLdOpts);
    }

    // ---------- Telegram Mini App reklam kapisi ----------
    // startapp = "{slug}" ya da "{slug}--ad" (reklam isaretli). Ad-gate:
    //  - "--ad" varsa ve Adsgram blok tanimliysa: once reklam, sonra habere gec.
    //  - yoksa: dogrudan habere gec. Her durumda 1 kez ve guvenli (hata/timeout -> yine acilir).
    public static string AdGatePage(SiteOptions o, string? block)
    {
        var safeBlock = (block ?? "").Replace("'", "").Replace("\"", "").Trim();
        const string tpl = @"<!DOCTYPE html><html lang='tr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,viewport-fit=cover'>
<meta name='robots' content='noindex,nofollow'>
<title>Yukleniyor...</title>
<script src='https://telegram.org/js/telegram-web-app.js'></script>
<script src='https://sad.adsgram.ai/js/sad.min.js'></script>
<style>html,body{height:100%;margin:0}body{display:grid;place-items:center;font-family:system-ui,Arial,sans-serif;background:#0e1116;color:#e7ebf1;text-align:center;padding:24px}.s{width:36px;height:36px;border:3px solid #2a3444;border-top-color:#e8552e;border-radius:50%;animation:r 1s linear infinite;margin:0 auto 16px}@keyframes r{to{transform:rotate(360deg)}}.t{font-size:15px;color:#95a0b1}</style>
</head><body><div><div class='s'></div><div class='t' id='t'>Haber hazirlaniyor...</div></div>
<script>
(function(){
  var BLOCK='__BLOCK__';
  var tg=null;
  try{ tg=window.Telegram&&Telegram.WebApp?Telegram.WebApp:null; if(tg){tg.ready();tg.expand();} }catch(e){}

  // start parametresini topla (Telegram start_param oncelikli, sonra URL)
  var raw='';
  try{ raw=(tg&&tg.initDataUnsafe&&tg.initDataUnsafe.start_param)||''; }catch(e){}
  if(!raw){ try{ var u=new URLSearchParams(location.search); raw=u.get('startapp')||u.get('tgWebAppStartParam')||u.get('slug')||''; }catch(e){} }

  // '--ad' isareti reklam istendigini gosterir
  var showAd=false, slug=raw;
  if(/--ad$/.test(raw)){ showAd=true; slug=raw.replace(/--ad$/,''); }
  // guvenlik: slug yalniz izinli karakterler
  slug=(slug||'').replace(/[^A-Za-z0-9_-]/g,'');

  var done=false;
  // startapp KISA kod (ContentItemId) taşır; /r/{kod} gerçek slug'a çevirir. (Eski slug'lar da /r ile çalışır.)
  function go(){ if(done)return; done=true; try{location.replace(slug?('/r/'+encodeURIComponent(slug)+'?ma=1'):'/blog?ma=1');}catch(e){location.href='/blog';} }

  // Reklam istenmiyorsa ya da altyapi yoksa: dogrudan habere
  if(!showAd||!BLOCK||!(window.Adsgram&&window.Adsgram.init)){ go(); return; }

  document.getElementById('t').textContent='Reklam yukleniyor...';
  var safety=setTimeout(go,12000); // reklam takilirsa haberi yine ac
  try{
    var ad=window.Adsgram.init({blockId:BLOCK});
    ad.show().then(function(){clearTimeout(safety);go();}).catch(function(){clearTimeout(safety);go();});
  }catch(e){ clearTimeout(safety); go(); }
})();
</script></body></html>";
        return tpl.Replace("__BLOCK__", safeBlock);
    }

    // ---------- SEO çıktıları ----------
    // Sitemap: post + KATEGORİ + ETİKET (SEO dokümanı §3 — sitemap post+kategori+etiket).
    public static string Sitemap(SiteOptions o, IReadOnlyList<SitemapEntry> posts,
        IReadOnlyList<PublicCategory> cats,
        IReadOnlyList<(Guid CategoryId, DateTimeOffset LastModified)> catMod,
        IReadOnlyList<string> tags)
    {
        var modMap = new Dictionary<Guid, DateTimeOffset>();
        foreach (var c in catMod) modMap[c.CategoryId] = c.LastModified;

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        var newestMod = posts.Count > 0 ? posts.Max(e => e.LastModified) : (DateTimeOffset?)null;
        var lastmodTag = newestMod is { } nm ? $"<lastmod>{nm:yyyy-MM-dd}</lastmod>" : "";
        sb.Append($"<url><loc>{Enc(Abs(o, "/blog"))}</loc>{lastmodTag}<changefreq>hourly</changefreq><priority>1.0</priority></url>\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/yazilar"))}</loc>{lastmodTag}<changefreq>daily</changefreq><priority>0.8</priority></url>\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/sosyal"))}</loc><changefreq>weekly</changefreq><priority>0.4</priority></url>\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/hakkimizda"))}</loc><changefreq>yearly</changefreq><priority>0.4</priority></url>\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/iletisim"))}</loc><changefreq>yearly</changefreq><priority>0.4</priority></url>\n");
        // Kategoriler (yalnız yazısı olanlar)
        foreach (var c in cats)
        {
            if (!modMap.TryGetValue(c.Id, out var last)) continue;
            sb.Append($"<url><loc>{Enc(Abs(o, $"/kategori/{Uri.EscapeDataString(c.Slug)}"))}</loc><lastmod>{last:yyyy-MM-dd}</lastmod><changefreq>daily</changefreq><priority>0.8</priority></url>\n");
        }
        // Yazılar
        foreach (var e in posts)
            sb.Append($"<url><loc>{Enc(Abs(o, $"/blog/{e.Slug}"))}</loc><lastmod>{e.LastModified:yyyy-MM-dd}</lastmod><changefreq>weekly</changefreq><priority>0.7</priority></url>\n");
        // Etiket iniş sayfaları
        foreach (var t in tags)
            sb.Append($"<url><loc>{Enc(Abs(o, $"/etiket/{Uri.EscapeDataString(t)}"))}</loc><changefreq>weekly</changefreq><priority>0.5</priority></url>\n");
        // Yasal sayfalar (güven sinyali)
        sb.Append($"<url><loc>{Enc(Abs(o, "/gizlilik-politikasi"))}</loc><changefreq>yearly</changefreq><priority>0.3</priority></url>\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/cerez-politikasi"))}</loc><changefreq>yearly</changefreq><priority>0.3</priority></url>\n");
        sb.Append("</urlset>");
        return sb.ToString();
    }

    /// <summary>PWA manifest — mobilde "ana ekrana ekle" + kurumsal görünüm.</summary>
    public static string Manifest(SiteOptions o) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["name"] = o.SiteName,
        ["short_name"] = o.SiteName.Length > 12 ? o.SiteName[..12] : o.SiteName,
        ["start_url"] = "/",
        ["display"] = "standalone",
        ["background_color"] = "#ffffff",
        ["theme_color"] = "#ffffff",
        ["icons"] = new object[] { new Dictionary<string, object?> { ["src"] = "/logo.svg", ["sizes"] = "any", ["type"] = "image/svg+xml", ["purpose"] = "any" } }
    }, JsonLdOpts);

    /// <summary>Marka logosu (SVG) — şema publisher.logo + paylaşım/AI önizlemeleri için sabit adres.</summary>
    public static string LogoSvg(SiteOptions o)
    {
        var letter = string.IsNullOrWhiteSpace(o.SiteName) ? "H" : o.SiteName.Trim()[..1].ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 512 512'>"
             + "<rect width='512' height='512' rx='96' fill='#E8552E'/>"
             + "<text x='256' y='340' font-family='Georgia,serif' font-size='280' font-weight='bold' text-anchor='middle' fill='white'>" + Enc(letter) + "</text></svg>";
    }

    /// <summary>Google News sitemap — SON 48 SAATİN yazıları (Google News/Keşfet kuralı). Boşsa boş urlset döner.</summary>
    public static string NewsSitemap(SiteOptions o, IReadOnlyList<BlogListItem> recent, DateTimeOffset now)
    {
        var cutoff = now.AddHours(-48);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:news=\"http://www.google.com/schemas/sitemap-news/0.9\">\n");
        foreach (var p in recent.Where(p => p.PublishedAt >= cutoff).Take(1000))
        {
            sb.Append("<url><loc>").Append(Enc(Abs(o, $"/blog/{p.Slug}"))).Append("</loc>");
            sb.Append("<news:news><news:publication><news:name>").Append(Enc(o.SiteName)).Append("</news:name>");
            sb.Append("<news:language>tr</news:language></news:publication>");
            sb.Append("<news:publication_date>").Append(p.PublishedAt.ToString("yyyy-MM-dd'T'HH:mm:ssK")).Append("</news:publication_date>");
            sb.Append("<news:title>").Append(Enc(p.Title)).Append("</news:title></news:news></url>\n");
        }
        sb.Append("</urlset>");
        return sb.ToString();
    }

    /// <summary>/llms.txt — AI asistanlarının siteyi hızla anlaması için markdown özet (llmstxt.org standardı).</summary>
    public static string LlmsTxt(SiteOptions o, IReadOnlyList<PublicCategory> cats, IReadOnlyList<BlogListItem> recent)
    {
        var b = new StringBuilder();
        b.Append("# ").Append(o.SiteName).Append('\n');
        b.Append("> ").Append(o.Description).Append('\n').Append('\n');
        b.Append("Türkçe haber ve içerik sitesi. İçerikler güncel kaynaklardan derlenir, yapay zekâ desteğiyle hazırlanır ve editör onayından geçer. ");
        b.Append("İçeriklerimizin AI yanıtlarında kaynak gösterilerek kullanılmasına açığız.\n\n");
        b.Append("## Ana Sayfalar\n");
        b.Append("- [Ana Sayfa](").Append(Abs(o, "/blog")).Append(")\n");
        b.Append("- [Tüm Yazılar](").Append(Abs(o, "/yazilar")).Append(")\n");
        b.Append("- [Hakkımızda](").Append(Abs(o, "/hakkimizda")).Append(")\n");
        b.Append("- [İletişim](").Append(Abs(o, "/iletisim")).Append(")\n");
        b.Append("- [RSS](").Append(Abs(o, "/feed.xml")).Append(")\n");
        if (cats.Count > 0)
        {
            b.Append("\n## Kategoriler\n");
            foreach (var c in cats)
                b.Append("- [").Append(c.Name).Append("](").Append(Abs(o, $"/kategori/{Uri.EscapeDataString(c.Slug)}")).Append(")\n");
        }
        if (recent.Count > 0)
        {
            b.Append("\n## Son Yazılar\n");
            foreach (var p in recent.Take(20))
                b.Append("- [").Append(p.Title).Append("](").Append(Abs(o, $"/blog/{p.Slug}")).Append("): ").Append(p.MetaDescription).Append('\n');
        }
        return b.ToString();
    }

    public static string Robots(SiteOptions o) =>
        "# AI asistan botlari (GPTBot, ClaudeBot, PerplexityBot, Google-Extended vb.) dahil TUM botlara aciktir;\n" +
        "# icerigimizin AI yanitlarinda kaynak gosterilmesini destekliyoruz. Ozet icin: /llms.txt\n" +
        "User-agent: *\nAllow: /\n" +
        "Disallow: /admin/\nDisallow: /api/\nDisallow: /ad-gate\nDisallow: /r/\nDisallow: /ara\nDisallow: /_diag/\n" +
        $"Sitemap: {Abs(o, "/sitemap.xml")}\n" +
        $"Sitemap: {Abs(o, "/news-sitemap.xml")}\n";

    public static string Feed(SiteOptions o, IReadOnlyList<FeedEntry> entries)
    {
        string X(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\"><channel>\n");
        sb.Append($"<title>{X(o.SiteName)}</title>\n<link>{X(Abs(o, "/blog"))}</link>\n<description>{X(o.Description)}</description>\n");
        sb.Append("<language>tr</language>\n");
        sb.Append($"<atom:link href=\"{X(Abs(o, "/feed.xml"))}\" rel=\"self\" type=\"application/rss+xml\"/>\n");
        foreach (var e in entries)
        {
            var url = Abs(o, $"/blog/{e.Slug}");
            sb.Append("<item>\n");
            sb.Append($"<title>{X(e.Title)}</title>\n<link>{X(url)}</link>\n<guid>{X(url)}</guid>\n");
            sb.Append($"<pubDate>{e.PublishedAt.ToString("r")}</pubDate>\n<description>{X(e.Summary)}</description>\n");
            sb.Append("</item>\n");
        }
        sb.Append("</channel></rss>");
        return sb.ToString();
    }
}
