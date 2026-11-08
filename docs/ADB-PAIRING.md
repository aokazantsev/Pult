# Сопряжение через adb (отладка)

Как подключить телефон к `Pult.exe` без камеры — для разработки и сквозных прогонов, которые ведёт
Claude. Работает только с отладочной сборкой приложения (`ru.aokazantsev.pult.debug`); в release приложения этого
пути нет. ПК — любая сборка `Pult.exe`. Решения — D-92 и D-94 в [`DECISIONS.md`](DECISIONS.md).

Что происходит: Claude командой `Pult.exe pair` ([`CONTROL.md`](CONTROL.md)) открывает скрытое окно сопряжения, получает
содержимое QR в ответе команды и передаёт его отладочной активности приложения через `adb shell am start`. Дальше идёт обычное сопряжение по
[`protocol/CRYPTO.md`](../protocol/CRYPTO.md) §3: заявка, ключи в Keystore, заверение секретом QR, `hello`.

## Команды (Bash-инструмент Claude)

Переменные Bash-инструмента между вызовами не живут, поэтому каждый блок начинается строкой окружения и
выполняется одним вызовом целиком. `$D` — `adb` через очередь устройств машины разработчика (голый `adb` там
отклоняется); вне этой машины `D=` (пусто: `$D adb …` становится `adb …`), а строку `slots.py … release` шага 6
пропустить. `$X` — путь к своей сборке `Pult.exe`.

1. **`Pult.exe` запущен** (уже запущенный не трогается):
   ```bash
   X=D:/dev/pets/windows/Pult/desktop/Pult.exe
   $X start
   ```
2. **Телефон не сопряжён.** Уже сопряжён (на экране список тредов или «Сверьте отпечаток») — стереть данные
   приложения, иначе в logcat отказ `failed: the phone is already paired …; run pm clear first`:
   ```bash
   P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe; D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"
   $D adb shell pm clear ru.aokazantsev.pult.debug
   ```
3. **Экран разбужен, разблокирован и не гаснет** (на Xiaomi пауза обязательна; `stay_on_while_plugged_in` вернёт
   шаг 6), журнал приложения очищен:
   ```bash
   P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe; D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"
   $D adb shell settings put global stay_on_while_plugged_in 7
   $D adb shell input keyevent KEYCODE_WAKEUP; sleep 1; $D adb shell wm dismiss-keyguard; $D adb logcat -c
   ```
4. **QR с ПК — в приложение.** `--folder` — папки, чьи треды увидит телефон (можно несколько); без них — папки
   последних тредов, как в окне по умолчанию:
   ```bash
   P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe; D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"; X=D:/dev/pets/windows/Pult/desktop/Pult.exe
   R=$($X pair --folder 'D:\dev\pets\windows\Pult\experiments\hooks\desktop-lab'); B=$(printf '%s' "$R" | grep -o '"qr_b64url":"[^"]*"' | cut -d'"' -f4)
   [ -n "$B" ] || { echo "нет QR: $R"; false; } &&
   $D adb shell am start -n ru.aokazantsev.pult.debug/ru.aokazantsev.pult.DebugPairActivity --es pult_qr_b64 "$B"
   ```
   Заявка и QR — за 1–3 с, на экране ПК ничего не появляется. Нет QR — `message` в ответе: сервер
   недоступен, ошибка ключей; `not_running` — шаг 1.
5. **Итог** — за 5–10 с:
   ```bash
   P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe; D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"; X=D:/dev/pets/windows/Pult/desktop/Pult.exe
   $X pairing --wait 30 | grep -o '"state":"[a-z]*"\|"pair":"[^"]*"'
   $D adb logcat -d -s PultDebugPair
   ```
   Успех: окно в состоянии `paired` с тем же `pair`, что в logcat, в logcat `paired: hello from the PC received`; дальше
   `$X status` — `relay.paired: true`, `relay.phone_on_screen: true`. Отказ — строка `failed: …` в logcat (причина та же,
   что показал бы экран сканера) или `no result in 150 s`; `no QR: …` — extra не передан или не base64url.
   Последняя строка `keys sent, waiting for hello` — приложение не на экране (экран уснул): разбудить по шагу 3 в
   пределах 120 с, `hello` дойдёт сам.

6. **Окно сопряжения на ПК закрыть, экран — как был, устройство отпустить** (и после отказа шагов 4–5: скрытое окно
   иначе держит заявку до срока QR, 10 минут):
   ```bash
   P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe; D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"; X=D:/dev/pets/windows/Pult/desktop/Pult.exe
   $X close-pairing
   $D adb shell settings put global stay_on_while_plugged_in 0
   $P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi release
   ```

QR берётся и уходит в телефон одним вызовом шага 4, поэтому всегда свежий; повторный шаг 4 выпускает новую заявку
и закрывает прежнее окно. Разорвать сессию — `$X unpair`.

## Что видно человеку

- ПК: окно не появляется (оно скрыто; клик «Подключить телефон…» закрывает его и открывает обычное);
  уведомление в трее «Телефон подключён, отпечаток …» — как при скане камерой. Отпечаток и пара — и в `$X status` (`relay.fingerprint`, `relay.pair`).
- Телефон: приложение выходит на передний план, «Сверьте отпечаток с экраном ПК» с отсчётом, затем список тредов.
  Отказ на экране не виден — только в logcat.
- Сверять 16 цифр не нужно (D-92); при желании — журнал ПК против экрана телефона (`uiautomator dump`).

## Вход

Отладочная сборка на телефоне без защищённой блокировки входит без запроса (D-93), и сопряжение проходит без касаний.
На телефоне с PIN или отпечатком приложение просит вход: до него поток не открывается и `hello` не принимается —
человек должен войти в течение 120 с после шага 4. Пока входа нет, logcat стоит на `keys sent, waiting for hello`
(через 150 с — `no result in 150 s`); вход между 120 и 150 с — `failed: the pair is gone before hello`, позже — пара
стирается при входе без строки в logcat.

## Защита

`DebugPairActivity` закрыта правом `android.permission.DUMP`: его держит `shell`, а стороннее приложение — нет
(уровень `signature|privileged|development`: такое право приложению выдаёт только `pm grant` с adb). Тем же правом
androidx закрывает свой `ProfileInstallReceiver`. Уровень на телефоне:
```bash
P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe; D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"
$D adb shell "pm list permissions -f -g | grep -A4 'permission:android.permission.DUMP$'"
```
`run-as <пакет> am start …` защиту не проверяет: `am` от имени приложения отказывает раньше, по несовпадению пакета
вызывающего (`com.android.shell does not belong to uid`), и так же отказал бы для любой активности.
