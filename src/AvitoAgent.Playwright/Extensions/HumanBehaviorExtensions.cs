using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Extensions;

/// <summary>
/// Действия только через DOM/JS — без Playwright Mouse/Keyboard/Click,
/// которые на Windows активируют окно Chrome (Page.bringToFront / CDP Input).
/// </summary>
public static class HumanBehaviorExtensions
{
    private const string JsClick = """
        el => {
          if (!el) return false;
          try { el.scrollIntoView({ block: 'center', inline: 'nearest' }); } catch {}
          const opts = { bubbles: true, cancelable: true, view: window };
          el.dispatchEvent(new MouseEvent('pointerdown', opts));
          el.dispatchEvent(new MouseEvent('mousedown', opts));
          el.dispatchEvent(new MouseEvent('pointerup', opts));
          el.dispatchEvent(new MouseEvent('mouseup', opts));
          el.dispatchEvent(new MouseEvent('click', opts));
          try { el.click(); } catch {}
          return true;
        }
        """;

    private const string JsScrollBy = """
        dy => {
          window.scrollBy(0, dy);
          return window.scrollY;
        }
        """;

    private const string JsMoveTo = """
        ([x, y]) => {
          window._avitoAgentX = x;
          window._avitoAgentY = y;
          const el = document.elementFromPoint(x, y);
          if (!el) return;
          const opts = { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y };
          el.dispatchEvent(new MouseEvent('mousemove', opts));
        }
        """;

    /// <summary>
    /// React патчит value на самом элементе и обновляет свой _valueTracker при присваивании.
    /// Из-за этого прямое el.value = … проглатывается: onChange не вызывается, состояние
    /// компонента остаётся пустым (на Avito кнопка «Найти» тогда ничего не делает).
    /// Пишем через сеттер прототипа — трекер остаётся со старым значением, и React видит ввод.
    /// </summary>
    private const string JsSetNativeValue = """
        const setNativeValue = (el, value) => {
          const proto = el instanceof HTMLTextAreaElement
            ? HTMLTextAreaElement.prototype
            : HTMLInputElement.prototype;
          const descriptor = Object.getOwnPropertyDescriptor(proto, 'value');
          if (descriptor && descriptor.set) {
            descriptor.set.call(el, value);
          } else {
            el.value = value;
          }
        };
        """;

    private const string JsTypeChar = $$"""
        (el, ch) => {
          if (!el) return;
          {{JsSetNativeValue}}
          try { el.focus({ preventScroll: true }); } catch { try { el.focus(); } catch {} }
          const opts = { bubbles: true, cancelable: true, view: window, key: ch, data: ch };
          el.dispatchEvent(new KeyboardEvent('keydown', opts));
          el.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, cancelable: true, data: ch, inputType: 'insertText' }));
          if (el.isContentEditable) {
            el.textContent = (el.textContent || '') + ch;
          } else if ('value' in el) {
            const start = el.selectionStart ?? el.value.length;
            const end = el.selectionEnd ?? el.value.length;
            const v = el.value || '';
            setNativeValue(el, v.slice(0, start) + ch + v.slice(end));
            const pos = start + ch.length;
            try { el.setSelectionRange(pos, pos); } catch {}
          }
          el.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: true, data: ch, inputType: 'insertText' }));
          el.dispatchEvent(new KeyboardEvent('keyup', opts));
        }
        """;

    private const string JsKey = $$"""
        key => {
          {{JsSetNativeValue}}
          const el = document.activeElement || document.body;
          const opts = { bubbles: true, cancelable: true, view: window, key };
          if (key === 'Enter') {
            opts.key = 'Enter';
            opts.code = 'Enter';
            opts.keyCode = 13;
            opts.which = 13;
          } else if (key === 'Backspace') {
            opts.key = 'Backspace';
            opts.code = 'Backspace';
            opts.keyCode = 8;
            opts.which = 8;
            if (el && 'value' in el) {
              const start = el.selectionStart ?? el.value.length;
              const end = el.selectionEnd ?? el.value.length;
              if (start !== end) {
                setNativeValue(el, el.value.slice(0, start) + el.value.slice(end));
                try { el.setSelectionRange(start, start); } catch {}
              } else if (start > 0) {
                setNativeValue(el, el.value.slice(0, start - 1) + el.value.slice(start));
                try { el.setSelectionRange(start - 1, start - 1); } catch {}
              }
              el.dispatchEvent(new InputEvent('input', { bubbles: true, cancelable: true, inputType: 'deleteContentBackward' }));
            }
          } else if (key === 'Escape') {
            opts.key = 'Escape';
            opts.code = 'Escape';
            opts.keyCode = 27;
            opts.which = 27;
          } else if (key === 'ArrowRight') {
            opts.key = 'ArrowRight';
            opts.code = 'ArrowRight';
            opts.keyCode = 39;
            opts.which = 39;
          } else if (key === 'Control+A') {
            if (el && 'value' in el) {
              try { el.select(); } catch {}
            }
            el.dispatchEvent(new KeyboardEvent('keydown', { ...opts, key: 'a', ctrlKey: true }));
            el.dispatchEvent(new KeyboardEvent('keyup', { ...opts, key: 'a', ctrlKey: true }));
            return;
          }
          el.dispatchEvent(new KeyboardEvent('keydown', opts));
          el.dispatchEvent(new KeyboardEvent('keypress', opts));
          el.dispatchEvent(new KeyboardEvent('keyup', opts));
        }
        """;

    public static async Task HumanPauseAsync(
        this IPage page,
        int baseMs,
        int jitterMs,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(page);
        var delay = baseMs;

        if (jitterMs > 0)
        {
            delay += Random.Shared.Next(0, jitterMs + 1);
        }

        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    public static async Task HumanPauseRangeAsync(
        this IPage page,
        int minMs,
        int maxMs,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(page);

        if (maxMs < minMs)
        {
            (minMs, maxMs) = (maxMs, minMs);
        }

        var delay = minMs == maxMs ? minMs : Random.Shared.Next(minMs, maxMs + 1);

        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    public static async Task HumanClickAsync(
        this IPage page,
        ILocator locator,
        int timeoutMs = 15_000,
        CancellationToken cancellationToken = default,
        bool isTopControl = false
    )
    {
        _ = isTopControl;
        await locator.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs,
            }
        );

        try
        {
            await locator.ScrollIntoViewIfNeededAsync(
                new LocatorScrollIntoViewIfNeededOptions { Timeout = timeoutMs }
            );
        }
        catch (PlaywrightException) { }

        await locator.EvaluateAsync(JsClick);
        await page.HumanPauseRangeAsync(80, 180, cancellationToken);
    }

    /// <summary>Клик по координатам через DOM — без Playwright Mouse (не активирует Chrome).</summary>
    public static async Task HumanClickAtAsync(
        this IPage page,
        float x,
        float y,
        CancellationToken cancellationToken = default
    )
    {
        await page.EvaluateAsync(
            """
            ([x, y]) => {
              const el = document.elementFromPoint(x, y);
              if (!el) return false;
              const opts = { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y };
              el.dispatchEvent(new MouseEvent('pointerdown', opts));
              el.dispatchEvent(new MouseEvent('mousedown', opts));
              el.dispatchEvent(new MouseEvent('pointerup', opts));
              el.dispatchEvent(new MouseEvent('mouseup', opts));
              el.dispatchEvent(new MouseEvent('click', opts));
              try { el.click(); } catch {}
              return true;
            }
            """,
            new object[] { x, y }
        );
        await page.HumanPauseRangeAsync(40, 90, cancellationToken);
    }

    public static async Task HumanTypeAsync(
        this IPage page,
        ILocator locator,
        string text,
        int timeoutMs = 15_000,
        CancellationToken cancellationToken = default,
        bool clickFirst = true,
        bool isTopControl = false
    )
    {
        if (clickFirst)
        {
            await page.HumanClickAsync(locator, timeoutMs, cancellationToken, isTopControl);
            await page.HumanPauseRangeAsync(200, 450, cancellationToken);
        }
        else
        {
            await locator.WaitForAsync(
                new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = timeoutMs,
                }
            );
        }

        for (var i = 0; i < text.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await locator.EvaluateAsync(JsTypeChar, text[i].ToString());
            if (i + 1 < text.Length)
            {
                // Печать как у человека: ~2–6 символов/сек с джиттером.
                await page.HumanPauseRangeAsync(120, 320, cancellationToken);
            }
        }
    }

    public static async Task HumanKeyPressAsync(
        this IPage page,
        string key,
        CancellationToken cancellationToken = default
    )
    {
        // Alt+ArrowLeft и подобные — через history, без CDP Keyboard (активирует Chrome).
        if (key.Equals("Alt+ArrowLeft", StringComparison.OrdinalIgnoreCase))
        {
            await page.EvaluateAsync("() => history.back()");
            await page.HumanPauseRangeAsync(40, 90, cancellationToken);
            return;
        }

        await page.EvaluateAsync(JsKey, key);
        await page.HumanPauseRangeAsync(40, 90, cancellationToken);
    }

    public static async Task HumanScrollAsync(
        this IPage page,
        int deltaY,
        CancellationToken cancellationToken = default
    )
    {
        await page.EvaluateAsync(JsScrollBy, deltaY);
        await page.HumanPauseRangeAsync(60, 140, cancellationToken);
    }

    public static async Task HumanScrollToBottomAsync(
        this IPage page,
        CancellationToken cancellationToken = default
    )
    {
        for (var i = 0; i < 8; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = await page.EvaluateAsync<double>("() => window.scrollY");
            await page.EvaluateAsync(JsScrollBy, Random.Shared.Next(500, 900));
            await page.HumanPauseRangeAsync(90, 200, cancellationToken);
            var after = await page.EvaluateAsync<double>("() => window.scrollY");
            if (Math.Abs(after - before) < 2)
            {
                break;
            }
        }
    }

    public static async Task MoveMouseDownThroughAsync(
        this IPage page,
        IReadOnlyList<(float X, float Y)> waypoints,
        CancellationToken cancellationToken = default,
        int pauseMinMs = 90,
        int pauseMaxMs = 220
    )
    {
        if (waypoints.Count == 0)
        {
            return;
        }

        var min = Math.Max(40, Math.Min(pauseMinMs, pauseMaxMs));
        var max = Math.Max(min, pauseMaxMs);

        foreach (var point in waypoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MoveMouseHumanAsync(page, point.X, point.Y, cancellationToken);
            await page.HumanPauseRangeAsync(min, max, cancellationToken);
        }
    }

    public static async Task MoveMouseHumanAsync(
        this IPage page,
        float x,
        float y,
        CancellationToken cancellationToken = default
    )
    {
        var steps = Random.Shared.Next(6, 14);
        var start = await page.EvaluateAsync<float[]>(
            "() => [window._avitoAgentX||400, window._avitoAgentY||300]"
        );
        var x0 = start is { Length: >= 2 } ? start[0] : 400f;
        var y0 = start is { Length: >= 2 } ? start[1] : 300f;

        for (var i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = i / (float)steps;
            var nx = x0 + (x - x0) * t;
            var ny = y0 + (y - y0) * t;
            await page.EvaluateAsync(JsMoveTo, new object[] { nx, ny });
            await page.HumanPauseRangeAsync(8, 18, cancellationToken);
        }
    }
}
