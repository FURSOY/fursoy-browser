using System.Globalization;
using System.Net;
using System.Text.Json;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Services;

public sealed record DownloadPageItem(
    string Id,
    string FileName,
    string SourceUrl,
    string SourceHost,
    string FullPath,
    string State,
    string Status,
    string FileType,
    int Progress,
    bool Indeterminate,
    bool Active,
    bool Paused,
    bool CanOpen,
    bool CanShow,
    bool CanRetry,
    string StartedAt);

public sealed record DownloadPageCommand(string? Scope, string? Action, string? Id);

public static class DownloadsPage
{
    public const string Address = "fursoy://downloads/";

    public static string Serialize(IEnumerable<DownloadEntry> downloads) => JsonSerializer.Serialize(
        downloads.Select(entry => new DownloadPageItem(
            entry.HistoryId.ToString("D"),
            entry.FileName,
            entry.SourceUrl,
            entry.SourceHost,
            entry.FullPath,
            entry.State.ToString(),
            entry.StatusText,
            entry.FileTypeLabel,
            entry.ProgressPercent,
            entry.IsIndeterminate,
            entry.IsActive,
            entry.IsPaused,
            entry.CanOpen,
            entry.CanShowInFolder,
            entry.CanRetry,
            entry.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture))));

    public static string BuildHtml(IEnumerable<DownloadEntry> downloads)
    {
        var json = Serialize(downloads);
        var title = Html(Strings.Downloads);
        var search = Html(Strings.DownloadSearchPlaceholder);
        var empty = Html(Strings.DownloadsEmpty);
        var clear = Html(Strings.ClearDownloadHistory);
        var active = Html(Strings.ActiveDownloads);
        var recent = Html(Strings.RecentDownloads);
        var pause = Html(Strings.DownloadPause);
        var resume = Html(Strings.DownloadResume);
        var cancel = Html(Strings.DownloadCancel);
        var open = Html(Strings.DownloadOpen);
        var show = Html(Strings.DownloadShowInFolder);
        var remove = Html(Strings.DownloadRemoveFromHistory);
        var retry = Html(Strings.DownloadRetry);
        var language = Html(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var background = AppTheme.GetCssColor("BackgroundColor");
        var surface = AppTheme.GetCssColor("SurfaceColor");
        var surfaceAlt = AppTheme.GetCssColor("SurfaceAltColor");
        var hover = AppTheme.GetCssColor("SurfaceHoverColor");
        var border = AppTheme.GetCssColor("BorderColor");
        var accent = AppTheme.GetCssColor("AccentColor");
        var text = AppTheme.GetCssColor("TextPrimaryColor");
        var secondary = AppTheme.GetCssColor("TextSecondaryColor");
        var destructive = AppTheme.GetCssColor("DestructiveColor");
        var font = Html(AppTheme.GetFontFamily("UiFontFamily"));

        return $$"""
            <!doctype html><html lang="{{language}}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1"><title>{{title}}</title>
            <style>
              :root{color-scheme:dark light}*{box-sizing:border-box}html,body{margin:0;min-height:100%;background:{{background}};color:{{text}};font-family:'{{font}}',sans-serif}
              body{padding:36px 24px 64px}.page{max-width:920px;margin:auto}.top{display:flex;align-items:center;gap:18px;margin-bottom:24px}.top h1{font-size:26px;font-weight:600;margin:0;white-space:nowrap}.search{flex:1;position:relative}.search input{width:100%;height:42px;padding:0 16px 0 42px;border:1px solid {{border}};border-radius:21px;background:{{surfaceAlt}};color:{{text}};outline:none;font-size:14px}.search input:focus{border-color:{{accent}}}.search:before{content:'⌕';position:absolute;left:16px;top:7px;font-size:22px;color:{{secondary}}}.clear{border:0;border-radius:8px;padding:9px 12px;background:transparent;color:{{secondary}};cursor:pointer}.clear:hover{background:{{hover}};color:{{text}}}
              section{margin-top:28px}h2{font-size:13px;font-weight:600;color:{{secondary}};margin:0 0 10px 8px}.card{overflow:hidden;border:1px solid {{border}};border-radius:14px;background:{{surface}}}.row{display:grid;grid-template-columns:48px minmax(0,1fr) auto;gap:14px;align-items:center;padding:14px 16px;border-bottom:1px solid {{border}}}.row:last-child{border-bottom:0}.file{width:44px;height:44px;border-radius:10px;background:{{surfaceAlt}};display:grid;place-items:center;color:{{accent}};font-size:10px;font-weight:700}.name{font-size:14px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.meta{font-size:12px;color:{{secondary}};white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:4px}.progress{height:3px;background:{{surfaceAlt}};border-radius:99px;overflow:hidden;margin-top:8px}.progress i{display:block;height:100%;background:{{accent}}}.progress.indeterminate i{width:35%!important;animation:slide 1.15s ease-in-out infinite}@keyframes slide{from{transform:translateX(-120%)}to{transform:translateX(340%)} }.actions{display:flex;gap:4px;align-items:center}.actions button{border:0;border-radius:7px;padding:7px 9px;background:transparent;color:{{secondary}};font-size:12px;cursor:pointer}.actions button:hover{background:{{hover}};color:{{text}}}.actions .danger:hover{color:{{destructive}}}.empty{padding:48px;text-align:center;color:{{secondary}};border:1px dashed {{border}};border-radius:14px}.hidden{display:none!important}
              *{scrollbar-width:thin;scrollbar-color:{{secondary}} transparent}::-webkit-scrollbar{width:10px}::-webkit-scrollbar-thumb{border:3px solid transparent;border-radius:99px;background:{{secondary}};background-clip:padding-box}
              @media(max-width:650px){body{padding:20px 12px}.top{flex-wrap:wrap}.top h1{width:100%}.row{grid-template-columns:42px minmax(0,1fr)}.actions{grid-column:2;flex-wrap:wrap} }
            </style></head><body><main class="page">
              <div class="top"><h1>{{title}}</h1><div class="search"><input id="search" type="search" placeholder="{{search}}" autocomplete="off"></div><button class="clear" onclick="send('clear')">{{clear}}</button></div>
              <div id="content"></div>
            </main><script>
              let items={{json}}; const labels={active:'{{active}}',recent:'{{recent}}',empty:'{{empty}}',pause:'{{pause}}',resume:'{{resume}}',cancel:'{{cancel}}',open:'{{open}}',show:'{{show}}',remove:'{{remove}}',retry:'{{retry}}'};
              const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
              function send(action,id){CefSharp.PostMessage({scope:'downloads',action,id:id||null})}
              function actionButton(action,id,label,cls=''){return `<button class="${cls}" onclick="send('${action}','${id}')">${label}</button>`}
              function row(d){let a='';if(d.Active)a+=actionButton(d.Paused?'resume':'pause',d.Id,d.Paused?labels.resume:labels.pause)+actionButton('cancel',d.Id,labels.cancel,'danger');else{if(d.CanOpen)a+=actionButton('open',d.Id,labels.open);if(d.CanShow)a+=actionButton('show',d.Id,labels.show);if(d.CanRetry)a+=actionButton('retry',d.Id,labels.retry);a+=actionButton('remove',d.Id,labels.remove,'danger')}return `<article class="row" data-id="${d.Id}"><div class="file">${esc(d.FileType)}</div><div><div class="name" title="${esc(d.FileName)}">${esc(d.FileName)}</div><div class="meta status">${esc(d.Status)} · ${esc(d.SourceHost||d.SourceUrl)} · ${esc(d.StartedAt)}</div>${d.Active?`<div class="progress${d.Indeterminate?' indeterminate':''}"><i style="width:${d.Progress}%"></i></div>`:''}</div><div class="actions">${a}</div></article>`}
              function section(label,list){return list.length?`<section><h2>${label}</h2><div class="card">${list.map(row).join('')}</div></section>`:''}
              function render(){const q=document.querySelector('#search').value.trim().toLocaleLowerCase();const filtered=items.filter(d=>!q||`${d.FileName} ${d.SourceUrl} ${d.SourceHost}`.toLocaleLowerCase().includes(q));const active=filtered.filter(d=>d.Active),history=filtered.filter(d=>!d.Active);document.querySelector('#content').innerHTML=section(labels.active,active)+section(labels.recent,history)||( `<div class="empty">${labels.empty}</div>`)}
              function needsStructureRefresh(oldItems,nextItems){if(oldItems.length!==nextItems.length)return true;return nextItems.some((n,i)=>{const o=oldItems[i];return !o||o.Id!==n.Id||o.Active!==n.Active||o.Paused!==n.Paused||o.Indeterminate!==n.Indeterminate||o.CanOpen!==n.CanOpen||o.CanShow!==n.CanShow||o.CanRetry!==n.CanRetry})}
              function updateProgress(nextItems){for(const d of nextItems){const el=document.querySelector(`[data-id="${d.Id}"]`);if(!el)continue;const status=el.querySelector('.status');if(status)status.textContent=`${d.Status} · ${d.SourceHost||d.SourceUrl} · ${d.StartedAt}`;const bar=el.querySelector('.progress i');if(bar)bar.style.width=`${d.Progress}%`} }
              document.querySelector('#search').addEventListener('input',render);window.fursoyDownloads={setData(data){const next=typeof data==='string'?JSON.parse(data):data;if(needsStructureRefresh(items,next)){items=next;render()}else{items=next;updateProgress(next)} } };render();
            </script></body></html>
            """;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
