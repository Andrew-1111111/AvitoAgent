namespace AvitoAgent.Playwright.Browser;

internal static class StealthScripts
{
    /// <summary>
    /// Убирает navigator.webdriver (CDP его включает) и не трогает остальной fingerprint Chrome.
    /// Подмена plugins/languages/chrome.runtime палится антиботом Avito.
    /// </summary>
    public const string Main = """
        (() => {
          try {
            const proto = Navigator.prototype;
            const descriptor = Object.getOwnPropertyDescriptor(proto, 'webdriver');
            if (!descriptor || descriptor.get || descriptor.value === true) {
              Object.defineProperty(proto, 'webdriver', {
                get: () => false,
                configurable: true
              });
            }
          } catch (_) { }

          try {
            if (navigator.webdriver) {
              Object.defineProperty(navigator, 'webdriver', {
                get: () => false,
                configurable: true
              });
            }
          } catch (_) { }

          try {
            // Не даём Avito открывать объявления в новой вкладке (иначе Chrome забирает фокус ОС).
            // Не location.assign здесь: при вызове из чужого кода это ломает ожидания Playwright.
            const nativeOpen = window.open;
            const patchedOpen = function open(url) {
              if (typeof url === 'string' && url.length) {
                window.location.href = url;
                return null;
              }
              return null;
            };

            // Антибот сверяет Function.prototype.toString: подменённая функция без маскировки
            // возвращает свой исходник вместо "[native code]" - явный признак бота.
            const nativeToString = Function.prototype.toString;
            Function.prototype.toString = function toString() {
              return this === patchedOpen
                ? nativeToString.call(nativeOpen)
                : nativeToString.call(this);
            };

            window.open = patchedOpen;
          } catch (_) { }
        })();
        """;
}
