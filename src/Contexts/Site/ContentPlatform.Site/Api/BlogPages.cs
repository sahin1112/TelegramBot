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
    :root{--bg:#fff;--fg:#15181d;--muted:#68707d;--line:#e9ebef;--soft:#f6f7f9;--accent:#e8552e;--card:#fff;--ring:rgba(232,85,46,.16);
      --serif:"Iowan Old Style","Palatino Linotype",Palatino,Georgia,"Times New Roman",serif;--sans:system-ui,-apple-system,"Segoe UI",Roboto,Arial,sans-serif;--maxw:1180px}
    @media(prefers-color-scheme:dark){:root{--bg:#0e1116;--fg:#e7ebf1;--muted:#95a0b1;--line:#242b36;--soft:#151a22;--card:#141920;--ring:rgba(232,85,46,.28)}}
    *{box-sizing:border-box}html{scroll-behavior:smooth}
    body{margin:0;font-family:var(--sans);background:var(--bg);color:var(--fg);line-height:1.7;-webkit-font-smoothing:antialiased}
    a{color:inherit;text-decoration:none}img{max-width:100%;display:block}
    .wrap{max-width:var(--maxw);margin:0 auto;padding:0 20px}.serif{font-family:var(--serif)}
    .progress{position:fixed;top:0;left:0;height:3px;width:0;background:var(--accent);z-index:60;transition:width .1s linear}
    .ad{display:flex;align-items:center;justify-content:center;min-height:var(--h,90px);border:1px solid var(--line);border-radius:12px;background:var(--soft);color:var(--muted);font-size:12px;margin:22px 0;overflow:hidden}
    .ad.box{--h:250px}.ad.rail{--h:600px}
    header.site{position:sticky;top:0;z-index:50;background:color-mix(in srgb,var(--bg) 88%,transparent);backdrop-filter:saturate(1.4) blur(10px);border-bottom:1px solid var(--line)}
    header.site .bar{display:flex;align-items:center;gap:20px;height:66px}
    .logo{font-family:var(--serif);font-weight:700;font-size:23px;letter-spacing:-.01em;white-space:nowrap}.logo b{color:var(--accent)}
    header nav.main{display:flex;gap:22px;margin-left:8px;flex:1}header nav.main a{color:var(--muted);font-size:15px;font-weight:500}header nav.main a:hover{color:var(--fg)}
    .icons{display:flex;gap:6px}.iconbtn{width:40px;height:40px;border-radius:10px;border:1px solid var(--line);background:var(--card);display:grid;place-items:center;cursor:pointer;color:var(--fg)}.iconbtn.menu{display:none}
    .hero{display:grid;grid-template-columns:1.05fr .95fr;gap:40px;align-items:center;padding:44px 0 30px}
    .eyebrow{color:var(--accent);font-weight:700;font-size:13px;letter-spacing:.1em;text-transform:uppercase}
    .hero h1{font-family:var(--serif);font-size:clamp(32px,5vw,54px);line-height:1.06;letter-spacing:-.02em;margin:14px 0 16px}
    .hero p.sub{color:var(--muted);font-size:18px;max-width:46ch;margin:0 0 24px}
    .btns{display:flex;gap:12px;flex-wrap:wrap;align-items:center}
    .btn{display:inline-flex;align-items:center;gap:8px;padding:13px 22px;border-radius:30px;font-weight:600;font-size:15px;cursor:pointer;border:1px solid transparent}
    .btn.pri{background:var(--accent);color:#fff}.btn.ghost{color:var(--fg)}.btn.ghost:hover{color:var(--accent)}
    .feat{position:relative;border-radius:20px;overflow:hidden;border:1px solid var(--line);background:var(--card);box-shadow:0 20px 50px -30px rgba(0,0,0,.4)}
    .feat .im{aspect-ratio:5/4;background:linear-gradient(135deg,#e8552e33,#0e1116);width:100%;object-fit:cover}
    .feat .cap{position:absolute;left:16px;right:16px;bottom:16px;background:color-mix(in srgb,var(--card) 92%,transparent);backdrop-filter:blur(8px);border:1px solid var(--line);border-radius:16px;padding:16px 18px}
    .feat .k{color:var(--accent);font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase}.feat .t{font-family:var(--serif);font-size:22px;line-height:1.2;margin:6px 0 8px}
    .feat .m{color:var(--muted);font-size:13px;display:flex;align-items:center;justify-content:space-between}.feat .go{width:36px;height:36px;border-radius:50%;background:var(--accent);color:#fff;display:grid;place-items:center;flex:none}
    .sec-h{display:flex;align-items:baseline;justify-content:space-between;margin:38px 0 16px;gap:16px}.sec-h h2{font-family:var(--serif);font-size:26px;margin:0}.sec-h a{color:var(--accent);font-weight:600;font-size:14px;white-space:nowrap}
    .ey{color:var(--muted);font-size:12px;letter-spacing:.1em;text-transform:uppercase;font-weight:700}
    .social{border-top:1px solid var(--line);border-bottom:1px solid var(--line);padding:22px 0;display:grid;grid-template-columns:220px 1fr;gap:24px;align-items:center;margin-top:8px}
    .social .lead h3{font-family:var(--serif);font-size:20px;margin:0 0 4px}.social .lead p{margin:0;color:var(--muted);font-size:14px}
    .chan{display:grid;grid-template-columns:repeat(5,1fr);gap:12px}.ch{border:1px solid var(--line);border-radius:14px;padding:14px;background:var(--card)}
    .ch .nm{font-weight:600;font-size:13px}.ch .n{font-size:22px;font-weight:800;margin:8px 0 2px;letter-spacing:-.02em}.ch .lbl{color:var(--muted);font-size:11px;text-transform:uppercase}.ch a.f{display:block;margin-top:10px;text-align:center;padding:8px;border-radius:9px;color:#fff;font-weight:600;font-size:13px;background:var(--accent)}
    .grid3{display:grid;grid-template-columns:repeat(3,1fr);gap:22px}
    .card{border:1px solid var(--line);border-radius:16px;overflow:hidden;background:var(--card);display:flex;flex-direction:column;transition:.15s}
    .card:hover{border-color:color-mix(in srgb,var(--accent) 40%,var(--line));transform:translateY(-2px)}
    .card .im{aspect-ratio:16/10;background:var(--soft);width:100%;object-fit:cover}.card .p{padding:15px 16px;display:flex;flex-direction:column;gap:7px;flex:1}
    .card .k{color:var(--accent);font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em}.card .t{font-family:var(--serif);font-size:19px;line-height:1.25;margin:0}.card .d{color:var(--muted);font-size:14px;margin:0}.card .m{color:var(--muted);font-size:12.5px;margin-top:auto}
    .card.big{grid-column:span 2;flex-direction:row}.card.big .im{width:46%;aspect-ratio:auto;min-height:220px}.card.big .p{justify-content:center}.card.big .t{font-size:24px}
    .nl{margin:44px 0 8px;border:1px solid var(--line);border-radius:20px;background:linear-gradient(135deg,var(--ring),transparent);padding:28px}
    .nl h3{font-family:var(--serif);font-size:24px;margin:0 0 6px}.nl p{margin:0 0 16px;color:var(--muted)}
    .nl form{display:flex;gap:10px;max-width:460px;flex-wrap:wrap}.nl input{flex:1;min-width:200px;padding:13px 14px;border:1px solid var(--line);border-radius:12px;background:var(--bg);color:var(--fg);font:inherit}.nl button{padding:13px 22px;border:none;border-radius:12px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}
    .crumbs{font-size:13px;color:var(--muted);padding:16px 0 4px;display:flex;gap:8px;flex-wrap:wrap}.crumbs a:hover{color:var(--accent)}.crumbs span{opacity:.5}
    .glayout{display:grid;grid-template-columns:minmax(0,1fr) 320px;gap:44px;padding:8px 0 40px;align-items:start}
    .article-col{min-width:0}
    .with-rail{display:grid;grid-template-columns:52px minmax(0,1fr);gap:20px}
    .share-rail{position:sticky;top:90px;display:flex;flex-direction:column;gap:10px;align-self:start}
    .share-rail a{width:42px;height:42px;border-radius:50%;border:1px solid var(--line);background:var(--card);display:grid;place-items:center;color:var(--muted);transition:.15s}
    .share-rail a:hover{color:#fff;background:var(--accent);border-color:var(--accent);transform:translateY(-2px)}
    .kicker{color:var(--accent);font-weight:700;font-size:13px;letter-spacing:.08em;text-transform:uppercase}
    h1.title{font-family:var(--serif);font-weight:700;font-size:clamp(30px,5vw,46px);line-height:1.12;letter-spacing:-.015em;margin:10px 0 14px}
    .dek{font-size:19px;color:var(--muted);line-height:1.6;margin:0 0 22px;max-width:60ch}
    .byline{display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:14px 0;border-top:1px solid var(--line);border-bottom:1px solid var(--line)}
    .avatar{width:42px;height:42px;border-radius:50%;background:linear-gradient(135deg,var(--accent),#f0894f);color:#fff;display:grid;place-items:center;font-weight:700;font-family:var(--serif)}
    .byline .who{font-weight:600}.byline .m{color:var(--muted);font-size:14px}
    .cover{width:100%;border-radius:16px;margin:22px 0 6px;aspect-ratio:16/9;object-fit:cover;background:var(--soft)}
    .body{font-size:18px}.body p{margin:18px 0}.body h2{font-family:var(--serif);font-size:27px;line-height:1.25;margin:36px 0 10px;scroll-margin-top:88px;letter-spacing:-.01em}
    .body h3{font-family:var(--serif);font-size:21px;margin:26px 0 8px;scroll-margin-top:88px}.body a{color:var(--accent);text-decoration:underline;text-underline-offset:2px}
    .body img{border-radius:12px;margin:20px 0}.body ul,.body ol{margin:16px 0;padding-left:22px}.body li{margin:7px 0}
    .body blockquote{margin:24px 0;padding:6px 20px;border-left:4px solid var(--accent);font-family:var(--serif);font-size:21px;line-height:1.5}
    .tags{display:flex;flex-wrap:wrap;gap:9px;margin:30px 0 6px}.tags a{background:var(--soft);border:1px solid var(--line);border-radius:20px;padding:6px 14px;font-size:13px;color:var(--muted)}.tags a:hover{color:var(--accent);border-color:var(--accent)}
    .share-inline{display:none;gap:10px;margin:22px 0}.share-inline a{flex:1;text-align:center;padding:11px;border:1px solid var(--line);border-radius:10px;color:var(--muted);font-size:14px;font-weight:600}
    .side{display:flex;flex-direction:column;gap:26px;position:sticky;top:90px}
    .panel{border:1px solid var(--line);border-radius:16px;background:var(--card);overflow:hidden}
    .panel h3{margin:0;padding:15px 18px;font-size:13px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted);border-bottom:1px solid var(--line)}
    .toc ol{list-style:none;margin:0;padding:8px 8px 12px;counter-reset:t}.toc li{counter-increment:t}
    .toc a{display:flex;gap:12px;padding:9px 12px;border-radius:10px;color:var(--muted);font-size:14.5px;line-height:1.4}.toc a::before{content:counter(t,decimal-leading-zero);color:var(--accent);font-weight:700}
    .toc a:hover{background:var(--soft);color:var(--fg)}.toc a.active{background:var(--ring);color:var(--fg);font-weight:600}.toc li.sub a{padding-left:30px;font-size:14px}
    .news{padding:20px 18px}.news h4{font-family:var(--serif);font-size:20px;margin:0 0 6px}.news p{margin:0 0 12px;color:var(--muted);font-size:14px}
    .news input{width:100%;padding:11px 12px;border:1px solid var(--line);border-radius:10px;background:var(--bg);color:var(--fg);font:inherit;margin-bottom:9px}.news button{width:100%;padding:11px;border:none;border-radius:10px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}.news small{display:block;text-align:center;color:var(--muted);margin-top:8px;font-size:12px}
    .related{border-top:1px solid var(--line);margin-top:40px;padding-top:24px}.related h2{font-family:var(--serif);font-size:24px;margin:0 0 16px}
    .rgrid{display:grid;grid-template-columns:repeat(3,1fr);gap:18px}.rcard{border:1px solid var(--line);border-radius:14px;overflow:hidden;background:var(--card)}.rcard .im{aspect-ratio:16/10;background:var(--soft);width:100%}.rcard .p{padding:12px 14px}.rcard .k{color:var(--accent);font-size:11px;font-weight:700;text-transform:uppercase}.rcard .t{font-weight:600;line-height:1.35;margin:5px 0 0;font-family:var(--serif)}
    .comments{border-top:1px solid var(--line);margin-top:40px;padding-top:24px}.comments h2{font-family:var(--serif);font-size:24px;margin:0 0 6px}
    .cmt{display:flex;gap:12px;padding:16px 0;border-bottom:1px solid var(--line)}.cmt .av{width:38px;height:38px;border-radius:50%;background:var(--soft);color:var(--muted);display:grid;place-items:center;font-weight:700;flex:none}.cmt .who{font-weight:600}.cmt .when{color:var(--muted);font-size:13px}.cmt p{margin:4px 0 0}
    form.cmt-form{display:grid;gap:10px;max-width:560px;margin-top:18px}form.cmt-form .row{display:grid;grid-template-columns:1fr 1fr;gap:10px}
    form.cmt-form input,form.cmt-form textarea{font:inherit;padding:11px 12px;border:1px solid var(--line);border-radius:10px;background:var(--bg);color:var(--fg)}form.cmt-form textarea{min-height:110px;resize:vertical}form.cmt-form button{justify-self:start;background:var(--accent);color:#fff;border:none;border-radius:10px;padding:11px 22px;font:inherit;font-weight:600;cursor:pointer}
    .note{color:var(--accent);font-weight:600}
    footer.site{border-top:1px solid var(--line);margin-top:48px;padding:28px 0;color:var(--muted);font-size:14px}footer.site .cols{display:flex;justify-content:space-between;gap:20px;flex-wrap:wrap}
    .anchor{position:fixed;left:0;right:0;bottom:0;z-index:55;display:none;background:var(--card);border-top:1px solid var(--line);padding:8px 12px;align-items:center;gap:10px}.anchor .ad{margin:0;flex:1;--h:56px}.anchor .x{width:26px;height:26px;border:1px solid var(--line);border-radius:7px;background:var(--bg);color:var(--muted);cursor:pointer;flex:none}
    @media(max-width:1024px){.hero{grid-template-columns:1fr;gap:24px}.social{grid-template-columns:1fr}.chan{grid-template-columns:repeat(3,1fr)}.grid3{grid-template-columns:repeat(2,1fr)}.glayout{grid-template-columns:1fr;gap:8px}.side{position:static}.side .rail-ad{display:none}.rgrid{grid-template-columns:repeat(2,1fr)}}
    @media(max-width:640px){header nav.main{display:none}.iconbtn.menu{display:grid}.chan{grid-template-columns:repeat(2,1fr)}.grid3{grid-template-columns:1fr}.card.big{grid-column:span 1;flex-direction:column}.card.big .im{width:100%;min-height:0;aspect-ratio:16/10}.with-rail{grid-template-columns:1fr}.share-rail{display:none}.share-inline{display:flex}.body{font-size:17px}.rgrid{grid-template-columns:1fr}.anchor{display:flex}main{padding-bottom:76px}form.cmt-form .row{grid-template-columns:1fr}}
    """;

    private const string BaseJs = """
    (function(){
      var pb=document.getElementById('progress');
      if(pb){addEventListener('scroll',function(){var h=document.documentElement;var st=h.scrollTop||document.body.scrollTop;var sh=(h.scrollHeight)-h.clientHeight;pb.style.width=(sh>0?st/sh*100:0)+'%';},{passive:true});}
      var links=[].slice.call(document.querySelectorAll('#toc a'));
      var heads=links.map(function(a){return document.getElementById(a.getAttribute('href').slice(1));}).filter(Boolean);
      if('IntersectionObserver' in window && heads.length){
        var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){links.forEach(function(l){l.classList.toggle('active',l.getAttribute('href')==='#'+e.target.id);});}});},{rootMargin:'-80px 0px -70% 0px'});
        heads.forEach(function(h){io.observe(h);});
      }
    })();
    """;

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
            + headExtra + Preconnect(o) + GscMeta(o) + AnalyticsHead(o) + adHead + "<style>" + BaseCss + "</style></head><body>"
            + "<div class=\"progress\" id=\"progress\"></div>"
            + "<header class=\"site\"><div class=\"wrap bar\"><a class=\"logo\" href=\"/blog\">" + Enc(o.SiteName) + "<b>.</b></a>"
            + "<nav class=\"main\"><a href=\"/blog\">Blog</a><a href=\"/feed.xml\">RSS</a></nav>"
            + "<div class=\"icons\"><a class=\"iconbtn\" href=\"/blog\" title=\"Ara\">🔍</a></div></div></header>"
            + "<main class=\"wrap\">" + bodyInner + "</main>"
            + "<footer class=\"site\"><div class=\"wrap cols\"><div>© " + DateTimeOffset.UtcNow.Year + " " + Enc(o.SiteName) + " · " + Enc(o.Description) + "</div>"
            + "<div><a href=\"/feed.xml\">RSS</a> · <a href=\"/sitemap.xml\">Site haritası</a>" + FooterLinks(o) + "</div></div></footer>"
            + ConsentBanner(o) + anchor + "<script>" + BaseJs + "</script></body></html>";
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
        var sb = new StringBuilder();
        sb.Append("<div class=\"ad ").Append(cssClass).Append("\" data-placement=\"").Append(placement).Append("\">");
        if (!string.IsNullOrWhiteSpace(o.AdSenseClient))
        {
            sb.Append("<ins class=\"adsbygoogle\" style=\"display:block;width:100%\" data-ad-client=\"")
              .Append(Enc(o.AdSenseClient)).Append("\" data-ad-format=\"auto\" data-full-width-responsive=\"true\"></ins>")
              .Append("<script>(adsbygoogle=window.adsbygoogle||[]).push({});</script>");
        }
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
        sb.Append("<meta name=\"robots\" content=\"index,follow\">");
        sb.Append($"<meta property=\"og:type\" content=\"{type}\"><meta property=\"og:title\" content=\"{Enc(title)}\">");
        sb.Append($"<meta property=\"og:description\" content=\"{Enc(description)}\"><meta property=\"og:url\" content=\"{Enc(canonical)}\">");
        sb.Append($"<meta property=\"og:site_name\" content=\"{Enc(o.SiteName)}\">");
        if (img.Length > 0) sb.Append($"<meta property=\"og:image\" content=\"{Enc(img)}\">");
        sb.Append($"<meta name=\"twitter:card\" content=\"{(img.Length > 0 ? "summary_large_image" : "summary")}\">");
        sb.Append($"<meta name=\"twitter:title\" content=\"{Enc(title)}\"><meta name=\"twitter:description\" content=\"{Enc(description)}\">");
        if (img.Length > 0) sb.Append($"<meta name=\"twitter:image\" content=\"{Enc(img)}\">");
        sb.Append($"<link rel=\"alternate\" type=\"application/rss+xml\" title=\"{Enc(o.SiteName)}\" href=\"{Enc(Abs(o, "/feed.xml"))}\">");
        if (jsonLd is not null) sb.Append($"<script type=\"application/ld+json\">{jsonLd}</script>");
        return sb.ToString();
    }

    // ---------- Ana sayfa ----------
    public static string Home(SiteOptions o, IReadOnlyList<BlogListItem> posts, int page, int totalPages)
    {
        var head = SeoHead(o, page > 1 ? $"{o.SiteName} — Sayfa {page}" : o.SiteName, o.Description, page > 1 ? $"/blog?page={page}" : "/blog", null, "website", null);
        var b = new StringBuilder();
        b.Append(AdSlot(o, "Header", ""));

        if (posts.Count == 0)
        {
            b.Append($"<h1 class=\"serif\">{Enc(o.SiteName)}</h1><p class=\"dek\">{Enc(o.Description)}</p><p class=\"meta\">Henüz yazı yok.</p>");
            return Layout(o, head, b.ToString());
        }

        // Hero + öne çıkan (en yeni yazı)
        if (page == 1)
        {
            var f = posts[0];
            b.Append("<section class=\"hero\"><div>");
            b.Append($"<div class=\"eyebrow\">{Enc(o.SiteName)}</div>");
            b.Append($"<h1 class=\"serif\">{Enc(o.Description)}</h1>");
            b.Append("<p class=\"sub\">Öne çıkan gündemi, analizleri ve rehberleri tek yerde topluyoruz.</p>");
            b.Append("<div class=\"btns\"><a class=\"btn pri\" href=\"/blog/").Append(Enc(f.Slug)).Append("\">Öne çıkanı oku →</a><a class=\"btn ghost\" href=\"/feed.xml\">RSS</a></div>");
            b.Append("</div>");
            b.Append($"<a class=\"feat\" href=\"/blog/{Enc(f.Slug)}\">");
            b.Append(CoverDiv(f.CoverImageUrl, "im"));
            b.Append("<div class=\"cap\">");
            b.Append($"<div class=\"k\">Öne Çıkan{Kicker(f.Tags, " · ")}</div>");
            b.Append($"<div class=\"t\">{Enc(f.Title)}</div>");
            b.Append($"<div class=\"m\"><span>{Enc(o.SiteName)} · {f.PublishedAt.ToLocalTime():dd MMMM yyyy}</span><span class=\"go\">→</span></div>");
            b.Append("</div></a></section>");
        }

        b.Append(SocialStrip(o));

        // Son yazılar
        var rest = (page == 1 ? posts.Skip(1) : posts).ToList();
        b.Append("<div class=\"sec-h\"><div><div class=\"ey\">Bugüne dair</div><h2 class=\"serif\">Son Yazılar</h2></div><a href=\"/blog\">Tümü →</a></div>");
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
        b.Append(Pager(page, totalPages, "/blog"));
        return Layout(o, head, b.ToString());
    }

    // ---------- Makale ----------
    public static string Post(SiteOptions o, BlogPostView p, IReadOnlyList<BlogListItem> related, IReadOnlyList<CommentView> comments, bool submitted)
    {
        var path = $"/blog/{p.Slug}";
        var head = SeoHead(o, p.Title, p.MetaDescription, path, p.CoverImageUrl, "article", ArticleJsonLd(o, p));
        var (bodyHtml, toc) = BuildToc(p.BodyHtml);
        var adsOk = o.AdsEnabled && CountWords(p.BodyHtml) >= o.AdsMinWords;
        bodyHtml = InjectAd(bodyHtml, AdSlot(o, "InArticle1", "box", allow: adsOk), 3);
        var minutes = ReadingMinutes(p.BodyHtml);
        var kick = p.Tags.Count > 0 ? p.Tags[0] : "Yazı";
        var shareUrl = Uri.EscapeDataString(Abs(o, path));
        var shareTitle = Uri.EscapeDataString(p.Title);

        var b = new StringBuilder();
        b.Append(AdSlot(o, "Header", ""));
        b.Append($"<div class=\"crumbs\"><a href=\"/blog\">Ana Sayfa</a><span>›</span>{Enc(kick)}<span>›</span>{Enc(p.Title)}</div>");
        b.Append("<div class=\"glayout\"><div class=\"article-col\"><div class=\"with-rail\">");

        // paylaşım rayı
        b.Append("<div class=\"share-rail\">");
        b.Append($"<a href=\"https://twitter.com/intent/tweet?url={shareUrl}&text={shareTitle}\" target=\"_blank\" rel=\"noopener\" title=\"X\">𝕏</a>");
        b.Append($"<a href=\"https://t.me/share/url?url={shareUrl}&text={shareTitle}\" target=\"_blank\" rel=\"noopener\" title=\"Telegram\">✈</a>");
        b.Append($"<a href=\"https://api.whatsapp.com/send?text={shareTitle}%20{shareUrl}\" target=\"_blank\" rel=\"noopener\" title=\"WhatsApp\">✆</a>");
        b.Append($"<a href=\"https://www.linkedin.com/sharing/share-offsite/?url={shareUrl}\" target=\"_blank\" rel=\"noopener\" title=\"LinkedIn\">in</a>");
        b.Append("</div>");

        b.Append("<article>");
        b.Append($"<div class=\"kicker\">{Enc(kick)}</div>");
        b.Append($"<h1 class=\"title\">{Enc(p.Title)}</h1>");
        b.Append($"<p class=\"dek\">{Enc(p.MetaDescription)}</p>");
        b.Append("<div class=\"byline\">");
        b.Append($"<div class=\"avatar\">{Enc(o.Author.Substring(0, 1).ToUpperInvariant())}</div>");
        b.Append($"<div><div class=\"who\">{Enc(o.Author)}</div><div class=\"m\">{p.PublishedAt.ToLocalTime():dd MMMM yyyy} · {minutes} dk okuma · {p.Views:N0} görüntülenme</div></div>");
        b.Append("</div>");
        if (!string.IsNullOrEmpty(p.CoverImageUrl))
            b.Append($"<img class=\"cover\" src=\"{Enc(p.CoverImageUrl)}\" alt=\"{Enc(p.CoverImageAlt ?? p.Title)}\">");
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
        b.Append("<div class=\"panel news\"><h4>Bültenimize Katılın</h4><p>Haftanın öne çıkanları e-postana gelsin.</p><form method=\"post\" action=\"#\"><input type=\"email\" placeholder=\"E-posta adresiniz\"><button type=\"button\">Bültene Katıl</button></form><small>İstediğin zaman iptal edebilirsin.</small></div>");
        if (related.Count > 0)
        {
            b.Append("<div class=\"panel\"><h3>Popüler Yazılar</h3><div style=\"padding:6px 8px 12px\">");
            foreach (var r in related.Take(4))
                b.Append($"<a href=\"/blog/{Enc(r.Slug)}\" style=\"display:block;padding:10px;border-radius:10px\"><div style=\"font-weight:600;font-size:14.5px;line-height:1.35\">{Enc(r.Title)}</div><div style=\"color:var(--muted);font-size:12.5px;margin-top:3px\">{r.PublishedAt.ToLocalTime():dd MMMM yyyy}</div></a>");
            b.Append("</div></div>");
        }
        var rail = AdSlot(o, "Sidebar", "rail", allow: adsOk);
        if (rail.Length > 0) b.Append("<div class=\"rail-ad\">" + rail + "</div>");
        b.Append("</aside></div>"); // side + glayout

        return Layout(o, head, b.ToString());
    }

    public static string Tag(SiteOptions o, string tag, IReadOnlyList<BlogListItem> posts)
    {
        var head = SeoHead(o, $"#{tag} — {o.SiteName}", $"{tag} etiketli yazılar", $"/etiket/{Uri.EscapeDataString(tag)}", null, "website", null);
        var b = new StringBuilder();
        b.Append($"<div class=\"sec-h\"><div><div class=\"ey\">Etiket</div><h2 class=\"serif\">#{Enc(tag)}</h2></div></div>");
        if (posts.Count == 0) b.Append("<p class=\"meta\">Bu etikette yazı yok.</p>");
        else { b.Append("<div class=\"grid3\">"); foreach (var p in posts) b.Append(Card(p)); b.Append("</div>"); }
        return Layout(o, head, b.ToString());
    }

    public static string NotFound(SiteOptions o)
    {
        var head = $"<title>Bulunamadı — {Enc(o.SiteName)}</title><meta name=\"robots\" content=\"noindex\">";
        return Layout(o, head, "<h1 class=\"serif\">Sayfa bulunamadı</h1><p><a class=\"note\" href=\"/blog\">Bloga dön →</a></p>");
    }

    // ---------- Parçalar ----------
    private static string Kicker(IReadOnlyList<string> tags, string prefix) =>
        tags.Count > 0 ? prefix + Enc(tags[0]) : "";

    private static string CoverDiv(string? url, string cls) =>
        string.IsNullOrEmpty(url)
            ? $"<div class=\"{cls}\"></div>"
            : $"<img class=\"{cls}\" src=\"{Enc(url)}\" alt=\"\" loading=\"lazy\">";

    private static string Card(BlogListItem p)
    {
        var sb = new StringBuilder("<a class=\"card\" href=\"/blog/").Append(Enc(p.Slug)).Append("\">");
        sb.Append(CoverDiv(p.CoverImageUrl, "im"));
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
        sb.Append(CoverDiv(p.CoverImageUrl, "im"));
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
        sb.Append(CoverDiv(p.CoverImageUrl, "im"));
        sb.Append("<div class=\"p\">");
        if (p.Tags.Count > 0) sb.Append($"<div class=\"k\">{Enc(p.Tags[0])}</div>");
        sb.Append($"<div class=\"t\">{Enc(p.Title)}</div>");
        sb.Append("</div></a>");
        return sb.ToString();
    }

    private static string Newsletter() =>
        "<section class=\"nl\"><h3 class=\"serif\">Bültenimize Katılın</h3><p>Haftanın öne çıkan gelişmelerini kaçırma, doğrudan e-postana gelsin.</p>"
        + "<form method=\"post\" action=\"#\"><input type=\"email\" placeholder=\"E-posta adresiniz\"><button type=\"button\">Bültene Katıl</button></form></section>";

    private static string SocialStrip(SiteOptions o)
    {
        var items = new List<string>();
        void Add(string? url, string? count, string name, string label)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var sb = new StringBuilder("<div class=\"ch\">");
            sb.Append($"<div class=\"nm\">{Enc(name)}</div>");
            if (!string.IsNullOrWhiteSpace(count)) sb.Append($"<div class=\"n\">{Enc(count)}</div>");
            sb.Append($"<div class=\"lbl\">{Enc(label)}</div>");
            sb.Append($"<a class=\"f\" href=\"{Enc(url)}\" target=\"_blank\" rel=\"noopener\">Takip Et</a></div>");
            items.Add(sb.ToString());
        }
        Add(o.TelegramUrl, o.TelegramMembers, "Telegram", "Üye");
        Add(o.XUrl, o.XFollowers, "X", "Takipçi");
        Add(o.InstagramUrl, o.InstagramFollowers, "Instagram", "Takipçi");
        Add(o.ThreadsUrl, o.ThreadsFollowers, "Threads", "Takipçi");
        Add(o.YoutubeUrl, o.YoutubeSubscribers, "YouTube", "Abone");
        if (items.Count == 0) return "";
        return "<section class=\"social\"><div class=\"lead\"><h3 class=\"serif\">Sosyalde " + Enc(o.SiteName) + "</h3><p>Anında paylaş, haberdar ol, topluluğa katıl.</p></div><div class=\"chan\">"
            + string.Concat(items) + "</div></section>";
    }

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

    private static string ArticleJsonLd(SiteOptions o, BlogPostView p)
    {
        var url = Abs(o, $"/blog/{p.Slug}");
        var article = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Article",
            ["headline"] = p.Title,
            ["description"] = p.MetaDescription,
            ["datePublished"] = p.PublishedAt.ToString("o"),
            ["dateModified"] = (p.UpdatedAt ?? p.PublishedAt).ToString("o"),
            ["mainEntityOfPage"] = url,
            ["url"] = url,
            ["author"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = o.Author },
            ["publisher"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = o.SiteName }
        };
        if (!string.IsNullOrEmpty(p.CoverImageUrl))
            article["image"] = Abs(o, p.CoverImageUrl!);

        var breadcrumb = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = new object[]
            {
                new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = "Blog", ["item"] = Abs(o, "/blog") },
                new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = p.Title, ["item"] = url }
            }
        };
        var opts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonSerializer.Serialize(new[] { article, breadcrumb }, opts);
    }

    // ---------- Telegram Mini App reklam kapisi ----------
    public static string AdGatePage(SiteOptions o, string? block)
    {
        const string tpl = @"<!DOCTYPE html><html lang='tr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Yukleniyor...</title>
<script src='https://telegram.org/js/telegram-web-app.js'></script>
<script src='https://sad.adsgram.ai/js/sad.min.js'></script>
<style>html,body{height:100%;margin:0}body{display:grid;place-items:center;font-family:system-ui,Arial,sans-serif;background:#0e1116;color:#e7ebf1;text-align:center}.s{width:34px;height:34px;border:3px solid #2a3444;border-top-color:#e8552e;border-radius:50%;animation:r 1s linear infinite;margin:0 auto 14px}@keyframes r{to{transform:rotate(360deg)}}</style>
</head><body><div><div class='s'></div><div>Haber hazirlaniyor...</div></div>
<script>
(function(){
  try{ if(window.Telegram&&Telegram.WebApp){Telegram.WebApp.ready();Telegram.WebApp.expand();} }catch(e){}
  var slug='';
  try{ slug=(Telegram.WebApp.initDataUnsafe&&Telegram.WebApp.initDataUnsafe.start_param)||''; }catch(e){}
  if(!slug){ var u=new URLSearchParams(location.search); slug=u.get('startapp')||u.get('slug')||''; }
  function go(){ location.href = slug ? ('/blog/'+encodeURIComponent(slug)) : '/blog'; }
  var BLOCK='__BLOCK__';
  if(!BLOCK||!window.Adsgram){ go(); return; }
  try{ var ad=window.Adsgram.init({blockId:BLOCK}); ad.show().then(go).catch(go); setTimeout(go,15000); }
  catch(e){ go(); }
})();
</script></body></html>";
        return tpl.Replace("__BLOCK__", (block ?? "").Replace("'", "").Replace("\"", ""));
    }

    // ---------- SEO çıktıları ----------
    public static string Sitemap(SiteOptions o, IReadOnlyList<SitemapEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        sb.Append($"<url><loc>{Enc(Abs(o, "/blog"))}</loc></url>\n");
        foreach (var e in entries)
            sb.Append($"<url><loc>{Enc(Abs(o, $"/blog/{e.Slug}"))}</loc><lastmod>{e.LastModified:yyyy-MM-dd}</lastmod></url>\n");
        sb.Append("</urlset>");
        return sb.ToString();
    }

    public static string Robots(SiteOptions o) =>
        $"User-agent: *\nAllow: /\nSitemap: {Abs(o, "/sitemap.xml")}\n";

    public static string Feed(SiteOptions o, IReadOnlyList<FeedEntry> entries)
    {
        string X(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<rss version=\"2.0\"><channel>\n");
        sb.Append($"<title>{X(o.SiteName)}</title>\n<link>{X(Abs(o, "/blog"))}</link>\n<description>{X(o.Description)}</description>\n");
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
