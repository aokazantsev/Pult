# Сопряжение через adb (отладка)

Как подключить телефон к `Pult.exe` без камеры и QR на экране — для разработки и сквозных прогонов, которые ведёт
Claude. Работает только с отладочной сборкой приложения (`ru.aokazantsev.pult.debug`); в release этого пути нет.
Решение — D-92 в [`DECISIONS.md`](DECISIONS.md).

Что происходит: Claude просит `Pult.exe` открыть окно сопряжения, берёт содержимое QR из файла и передаёт его
отладочной активности приложения через `adb shell am start`. Дальше идёт обычное сопряжение по
[`protocol/CRYPTO.md`](../protocol/CRYPTO.md) §3: заявка, ключи в Keystore, заверение секретом QR, `hello`.

## Команды (Bash-инструмент Claude)

На машине разработчика `adb` — только через очередь устройств:
`P=C:/Users/aokaz/AppData/Local/Python/pythoncore-3.14-64/python.exe`,
`D="$P C:/Users/aokaz/dev-config/claude/hooks/slots.py device --device redmi --"`. Вне этой машины — просто `adb`.

1. **`Pult.exe` запущен.** Нет в процессах — `powershell -c "Start-Process D:\dev\pets\windows\Pult\desktop\Pult.exe"`.
2. **Телефон не сопряжён.** Уже сопряжён (на экране список тредов или «Сверьте отпечаток») — стереть данные
   приложения, иначе отказ «Пара уже есть»:
   ```bash
   $D adb shell pm clear ru.aokazantsev.pult.debug
   ```
3. **Экран разбужен, разблокирован и не гаснет** (на Xiaomi пауза обязательна; `device release` вернёт
   `stay_on_while_plugged_in` в 0 сам), журнал приложения очищен:
   ```bash
   $D adb shell settings put global stay_on_while_plugged_in 7
   $D adb shell input keyevent KEYCODE_WAKEUP; sleep 1; $D adb shell wm dismiss-keyguard; $D adb logcat -c
   ```
4. **Запрос сопряжения на ПК** — папки, чьи треды увидит телефон, по строке `folder=`; без строк — папки последних
   тредов, как в окне по умолчанию:
   ```bash
   DATA="$LOCALAPPDATA/Pult"; rm -f "$DATA/pair-qr.txt"
   printf 'folder=%s\n' 'D:\dev\pets\windows\Pult\experiments\hooks\desktop-lab' > "$DATA/pair-request.txt"
   for i in $(seq 30); do [ -s "$DATA/pair-qr.txt" ] && break; sleep 1; done; ls -l "$DATA/pair-qr.txt"
   ```
   Трей открывает окно «Подключить телефон…» за секунду, заявка на сервере и QR — ещё за 1–3 с. Файла нет через
   30 с — смотреть `log.txt` (строки `pair request:` и `pairing:`): сервер недоступен, порт, ошибка ключей.
5. **QR — в приложение:**
   ```bash
   B=$(tr -d '\r\n' < "$DATA/pair-qr.txt" | base64 -w0 | tr '+/' '-_' | tr -d '=')
   $D adb shell am start -n ru.aokazantsev.pult.debug/ru.aokazantsev.pult.DebugPairActivity --es pult_qr_b64 "$B"
   ```
6. **Итог** — за 5–10 с:
   ```bash
   grep -a "relay: paired" "$DATA/log.txt" | tail -1
   $D adb logcat -d -s PultDebugPair
   ```
   Успех: на ПК `relay: paired <pair>, fingerprint …`, в logcat `paired: hello from the PC received`, `pair-qr.txt`
   исчез. Отказ — строка `failed: …` в logcat (причина та же, что показал бы экран сканера) или `no result in 150 s`.
   Последняя строка `keys sent, waiting for hello` — приложение не на экране (экран уснул): разбудить по шагу 3 в
   пределах 120 с, `hello` дойдёт сам.

QR живёт 10 минут и перевыпускается окном сам за 20 с до срока — `pair-qr.txt` при этом переписывается, брать
свежий. Окно на ПК после сопряжения остаётся открытым с отпечатком; следующий запрос закрывает его сам.

## Что видно человеку

- ПК: окно «Подключить телефон…» открывается само, показывает QR, затем «Телефон подключён» и отпечаток; уведомление
  в трее — как при скане камерой.
- Телефон: приложение выходит на передний план, «Сверьте отпечаток с экраном ПК» с отсчётом, затем список тредов.
  Отказ на экране не виден — только в logcat.
- Сверять 16 цифр не нужно (D-92); при желании — журнал ПК против экрана телефона (`uiautomator dump`).

## Вход

Отладочная сборка на телефоне без защищённой блокировки входит без запроса (D-93), и сопряжение проходит без касаний.
На телефоне с PIN или отпечатком приложение просит вход: до него поток не открывается и `hello` не принимается —
человек должен войти в течение 120 с после шага 5, иначе logcat покажет `failed: the pair is gone before hello`.

## Защита

`DebugPairActivity` закрыта правом `android.permission.DUMP`: его держит `shell`, а стороннее приложение — нет
(уровень `signature|privileged|development`: такое право приложению выдаёт только `pm grant` с adb). Тем же правом
androidx закрывает свой `ProfileInstallReceiver`. Уровень на телефоне:
```bash
$D adb shell "pm list permissions -f -g | grep -A4 'permission:android.permission.DUMP$'"
```
`run-as <пакет> am start …` защиту не проверяет: `am` от имени приложения отказывает раньше, по несовпадению пакета
вызывающего (`com.android.shell does not belong to uid`), и так же отказал бы для любой активности.
