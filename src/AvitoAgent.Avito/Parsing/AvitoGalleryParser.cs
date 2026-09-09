using AvitoAgent.Playwright.Extensions;
using AvitoAgent.Shared.Configuration;
using Microsoft.Playwright;

namespace AvitoAgent.Avito.Parsing;

internal static class AvitoGalleryParser
{
    private const int SafetyMaxPhotos = 50;

    private static readonly string[] NextPhotoSelectors =
    [
        "[data-marker='image-frame/right-button']",
        "button[data-marker='image-frame/right-button']",
        "[data-marker='extended-gallery/next']",
        "button[aria-label='Следующее фото']",
        "button[aria-label='Следующее изображение']",
        "button[aria-label*='Следующ']",
    ];

    public static async Task<(IReadOnlyList<string> Urls, int TotalCount)> CollectPhotosAsync(
        IPage page,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var galleryUrls = new List<(string Url, int Width)>();
        var pageUrls = new List<(string Url, int Width)>();
        var maxPhotos =
            options.MaxGalleryPhotos <= 0
                ? SafetyMaxPhotos
                : Math.Min(options.MaxGalleryPhotos, SafetyMaxPhotos);

        await HarvestRawAsync(page, galleryUrls, pageUrls);

        await page.HumanPauseAsync(
            options.GalleryPhotoDelayMs,
            options.GalleryPhotoDelayJitterMs,
            cancellationToken
        );

        var slides = Math.Max(1, await CountGallerySlidesAsync(page));
        var clicks = Math.Min(maxPhotos, slides) - 1;
        for (var i = 0; i < clicks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryClickNextPhotoAsync(page, options, cancellationToken))
            {
                break;
            }

            await HarvestRawAsync(page, galleryUrls, pageUrls);
            await page.HumanPauseAsync(
                options.GalleryPhotoDelayMs,
                options.GalleryPhotoDelayJitterMs,
                cancellationToken
            );
        }

        var urls = SelectBest(galleryUrls, pageUrls, maxPhotos);
        var total = Math.Max(slides, urls.Count);
        return (urls, total);
    }

    private static IReadOnlyList<string> SelectBest(
        IReadOnlyList<(string Url, int Width)> galleryUrls,
        IReadOnlyList<(string Url, int Width)> pageUrls,
        int maxPhotos
    )
    {
        var highRes = MergeUniqueHighest(
            pageUrls.Where(static x => x.Width >= 1280),
            maxPhotos
        );
        if (highRes.Count > 0)
        {
            return highRes;
        }

        return MergeUniqueHighest(pageUrls.Concat(galleryUrls), maxPhotos);
    }

    private static IReadOnlyList<string> MergeUniqueHighest(
        IEnumerable<(string Url, int Width)> urls,
        int maxPhotos
    )
    {
        var bestByPhoto = new Dictionary<string, (string Url, int Quality, int Order)>(
            StringComparer.OrdinalIgnoreCase
        );
        var order = 0;

        foreach (var (raw, width) in urls)
        {
            var upgraded = AvitoImageUrl.NormalizeAndUpgrade(raw);
            if (string.IsNullOrWhiteSpace(upgraded) || AvitoImageUrl.IsPreview(upgraded))
            {
                continue;
            }

            var identity = AvitoImageUrl.Identity(upgraded);
            var quality = Math.Max(width, AvitoImageUrl.Quality(upgraded));
            if (quality <= 0 || string.IsNullOrWhiteSpace(identity))
            {
                continue;
            }

            if (!bestByPhoto.TryGetValue(identity, out var existing))
            {
                bestByPhoto[identity] = (upgraded, quality, order++);
                continue;
            }

            if (quality > existing.Quality)
            {
                bestByPhoto[identity] = (upgraded, quality, existing.Order);
            }
        }

        return
        [
            .. bestByPhoto
                .Values.OrderBy(static x => x.Order)
                .Select(static x => x.Url)
                .Take(maxPhotos),
        ];
    }

    private static async Task HarvestRawAsync(
        IPage page,
        List<(string Url, int Width)> galleryUrls,
        List<(string Url, int Width)> pageUrls
    )
    {
        try
        {
            var raw = await page.EvaluateAsync<JsHarvest>(CollectUrlsScript) ?? new JsHarvest();
            if (raw.Gallery is { Length: > 0 })
            {
                foreach (var item in raw.Gallery)
                {
                    if (!string.IsNullOrWhiteSpace(item?.Url))
                    {
                        galleryUrls.Add((item.Url, item.Width));
                    }
                }
            }

            if (raw.Page is { Length: > 0 })
            {
                foreach (var item in raw.Page)
                {
                    if (!string.IsNullOrWhiteSpace(item?.Url))
                    {
                        pageUrls.Add((item.Url, item.Width));
                    }
                }
            }
        }
        catch (PlaywrightException) { }
    }

    private static async Task<int> CountGallerySlidesAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<int>(
                """
                () => {
                  const counter = document.querySelector(
                    '[data-marker="image-frame/counter"], [data-marker="extended-gallery/counter"], [data-marker*="gallery"] [data-marker*="counter"]'
                  );
                  if (counter) {
                    const t = (counter.textContent || '').replace(/\s+/g, ' ').trim();
                    const pair = t.match(/(\d+)\s*[\/изof]+\s*(\d+)/i);
                    if (pair) return parseInt(pair[2], 10) || 1;
                    const of = t.match(/(?:из|of)\s*(\d+)/i);
                    if (of) return parseInt(of[1], 10) || 1;
                  }
                  const thumbs = document.querySelectorAll(
                    '[data-marker="image-frame/thumbs"] [data-marker*="item"], [data-marker="extended-gallery/thumbs"] [data-marker*="item"], [data-marker="image-frame/thumbs"] button, [data-marker="image-frame/thumbs"] li'
                  );
                  return thumbs.length > 0 ? thumbs.length : 1;
                }
                """
            );
        }
        catch (PlaywrightException)
        {
            return 1;
        }
    }

    private static async Task<bool> TryClickNextPhotoAsync(
        IPage page,
        AvitoOptions options,
        CancellationToken cancellationToken
    )
    {
        foreach (var selector in NextPhotoSelectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var button = page.Locator(selector).First;
                if (await button.CountAsync() == 0 || !await button.IsVisibleAsync())
                {
                    continue;
                }

                await page.HumanClickAsync(
                    button,
                    timeoutMs: Math.Min(options.NavigationTimeoutMs, 3_000),
                    cancellationToken,
                    isTopControl: true
                );
                return true;
            }
            catch (PlaywrightException) { }
        }

        try
        {
            await page.HumanKeyPressAsync("ArrowRight", cancellationToken);
            await page.HumanPauseRangeAsync(120, 220, cancellationToken);
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private sealed class JsHarvest
    {
        public JsSizedUrl[]? Gallery { get; set; }

        public JsSizedUrl[]? Page { get; set; }
    }

    private sealed class JsSizedUrl
    {
        public string? Url { get; set; }

        public int Width { get; set; }
    }

    private const string CollectUrlsScript =
        """
        () => {
          const gallery = [];
          const page = [];
          const cleanUrl = (u) => {
            if (typeof u !== 'string' || u.length < 8 || u.startsWith('data:')) return '';
            let v = u.replace(/\\u002[fF]/g, '/').replace(/\\\//g, '/').replace(/&amp;/g, '&').trim();
            if (v.startsWith('//')) v = 'https:' + v;
            return v.length > 8 ? v : '';
          };
          const add = (list, u, width) => {
            const url = cleanUrl(u);
            if (!url) return;
            const existing = list.find(x => x.url === url);
            if (existing) {
              if ((width || 0) > existing.width) existing.width = width;
              return;
            }
            list.push({ url, width: width || 0 });
          };

          const listingId = (() => {
            const p = (location.pathname || '').replace(/\/+$/, '');
            const m = p.match(/_(\d{6,12})$/) || p.match(/\/(\d{6,12})$/);
            return m ? m[1] : '';
          })();

          const isAvitoImage = (u) => /avito\.st|img\.avito|img\.k\.avito|\/image\/1\//i.test(u);
          const isSizeKey = (k) => typeof k === 'string' && /^\d{2,4}x\d{2,4}$/.test(k);
          const sizeWidth = (k) => parseInt(k, 10) || 0;

          const pickBestSizeMap = (obj) => {
            let best = '', bestW = -1, n = 0;
            for (const [k, v] of Object.entries(obj)) {
              if (!isSizeKey(k) || typeof v !== 'string') continue;
              const url = cleanUrl(v);
              if (!url.startsWith('http')) continue;
              n++;
              const w = sizeWidth(k);
              if (w > bestW) { bestW = w; best = url; }
            }
            return n >= 1 && bestW >= 640 ? { url: best, width: bestW } : null;
          };

          const harvestTextMaps = (text) => {
            if (!text || text.length < 20 || text.length > 4000000) return;
            const decoded = text
              .replace(/\\u002F/gi, '/')
              .replace(/\\\//g, '/')
              .replace(/\\"/g, '"');
            const sizeMap = /"(32x32|75x75|100x75|140x105|208x156|240x180|320x240|432x324|480x360|640x480|644x483|832x624|1280x960|1440x1080|1600x1200|1920x1440|2560x1920)"\s*:\s*"(https?:[^"]+|\/\/[^"]+)"/gi;
            let current = [];
            const flush = () => {
              if (!current.length) return;
              current.sort((a, b) => b.w - a.w);
              if (current[0].w >= 640) add(page, current[0].u, current[0].w);
              current = [];
            };
            let m;
            while ((m = sizeMap.exec(decoded))) {
              const w = parseInt(m[1], 10);
              if (current.some(x => x.w === w)) flush();
              current.push({ w, u: m[2] });
            }
            flush();
          };

          const largestSrcset = (srcset) => {
            if (!srcset) return;
            let best = '', bestW = -1;
            for (const part of srcset.split(',')) {
              const tokens = part.trim().split(/\s+/);
              const u = tokens[0];
              if (!u) continue;
              const wToken = tokens[1] || '';
              const w = wToken.endsWith('w') ? parseInt(wToken, 10) : 0;
              if (w >= bestW) { bestW = w; best = u; }
            }
            add(gallery, best, bestW > 0 ? bestW : 0);
          };

          const takeImg = (img) => {
            const nw = Number(img.naturalWidth) || 0;
            const nh = Number(img.naturalHeight) || 0;
            const side = Math.max(nw, nh);
            add(gallery, img.currentSrc, side);
            add(gallery, img.src, side);
            add(gallery, img.getAttribute('data-src'), side);
            add(gallery, img.getAttribute('data-url'), side);
            add(gallery, img.getAttribute('data-large'), side);
            add(gallery, img.getAttribute('data-original'), side);
            largestSrcset(img.getAttribute('srcset') || img.getAttribute('data-srcset'));
          };

          const takeBg = (el) => {
            const style = el.getAttribute && el.getAttribute('style') || '';
            const bg = style.match(/url\((['"]?)(.*?)\1\)/i);
            if (bg) add(gallery, bg[2], 0);
          };

          const roots = [
            document.querySelector('[data-marker="item-view/gallery"]'),
            document.querySelector('[data-marker="image-frame/image-wrapper"]'),
            document.querySelector('[data-marker="image-frame"]'),
            document.querySelector('[data-marker="extended-gallery"]'),
            document.querySelector('[class*="gallery-extended"]'),
            document.querySelector('[data-marker="image-frame/thumbs"]'),
            document.querySelector('[data-marker*="image-preview"]')
          ].filter(Boolean);

          if (roots.length === 0 && document.body) roots.push(document.body);

          for (const root of roots) {
            for (const img of root.querySelectorAll('img')) takeImg(img);
            for (const source of root.querySelectorAll('source[srcset]')) {
              largestSrcset(source.getAttribute('srcset'));
            }
            for (const el of root.querySelectorAll('[style*="url("]')) takeBg(el);
          }

          for (const img of document.querySelectorAll('img[itemprop="image"]')) takeImg(img);

          const parseMaybeJson = (raw) => {
            if (!raw) return null;
            if (typeof raw === 'object') return raw;
            if (typeof raw !== 'string' || raw.length < 8) return null;
            try { return JSON.parse(raw); } catch {}
            try { return JSON.parse(decodeURIComponent(raw)); } catch {}
            return null;
          };

          const jsonRoots = [];
          for (const key of ['__initialData__', '__STATE__', '__data', '__PRELOADED_STATE__']) {
            try {
              const parsed = parseMaybeJson(window[key]);
              if (parsed) jsonRoots.push(parsed);
            } catch {}
          }
          for (const script of document.querySelectorAll('script[type="application/json"], script[type="application/ld+json"], script#__NEXT_DATA__')) {
            try {
              const parsed = parseMaybeJson(script.textContent);
              if (parsed) jsonRoots.push(parsed);
            } catch {}
          }

          const seen = new Set();
          const sameId = (value) => listingId && value != null && String(value) === listingId;

          const walk = (node, depth, inItem) => {
            if (node == null || depth > 18) return;
            if (typeof node === 'string') {
              if (inItem && isAvitoImage(node) && /\/(?:2560x1920|1920x1440|1600x1200|1440x1080|1280x960)(?:\/|\?|$)/i.test(node)) {
                add(page, node, 1280);
              }
              return;
            }
            if (typeof node !== 'object') return;
            if (seen.has(node)) return;
            seen.add(node);

            if (!Array.isArray(node)) {
              const mapped = pickBestSizeMap(node);
              if (mapped) add(page, mapped.url, mapped.width);

              const nodeInItem = inItem
                || sameId(node.id)
                || sameId(node.itemId)
                || sameId(node.item_id)
                || sameId(node.value && node.value.id);

              for (const [k, v] of Object.entries(node)) {
                const keyHasId = listingId && String(k).includes(listingId);
                const keyHint = /^(images|imageList|photos|gallery|image|media|pictures|urls)$/i.test(k);
                walk(v, depth + 1, nodeInItem || keyHasId || (nodeInItem && keyHint));
              }
              return;
            }

            for (const item of node) walk(item, depth + 1, inItem);
          };

          for (const root of jsonRoots) walk(root, 0, !listingId);

          for (const script of document.querySelectorAll('script')) {
            const src = script.textContent || '';
            if (src.length > 80 && /1280x960|640x480|2560x1920|image\/1\//i.test(src)) {
              harvestTextMaps(src);
            }
          }

          return { gallery, page };
        }
        """;
}
