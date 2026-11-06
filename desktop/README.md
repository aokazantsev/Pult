# Pult desktop

Программа в трее и хук-программа агента. Этап 1: без шифрования, пара задаётся вручную.

| Файл | Что делает |
|---|---|
| `Pult.exe` | трей: приём хуков на `127.0.0.1:<hook_port>`, поток SSE и `POST` к серверу-реле, режим «с телефона / с ПК» |
| `PultHook.exe` | хук-команда агента: событие из stdin → `Pult.exe`, ответ → stdout в UTF-8 |

Сборка — `build.cmd nostart` (оба exe), тесты логики — `tests\run.cmd`.

## Пара этапа 1

1. На сервере: `python3 -m pult_relay add-pair --id dev` — печатает `pc_token` и `phone_token`.
2. `%LOCALAPPDATA%\Pult\settings.txt` (создаётся при первом запуске, открывается из меню трея):

```
hook_port=47652
wait_seconds=300
server=http://127.0.0.1:8082
pair=dev
pc_token=<pc_token>
```

`wait_seconds` — ожидание Pult (10–540 с), меньше `timeout` хука. Без `server`, `pair`, `pc_token` хуки сразу
отпускают агента. После правки — перезапуск `Pult.exe`.

## Хуки Claude Code

Проектный `.claude/settings.local.json` (или пользовательский `~/.claude/settings.json`), путь — к своей сборке:

```json
{
  "hooks": {
    "PermissionRequest": [{ "hooks": [{ "type": "command", "command": "\"D:/path/Pult/desktop/PultHook.exe\"", "timeout": 600 }] }],
    "Stop": [{ "hooks": [{ "type": "command", "command": "\"D:/path/Pult/desktop/PultHook.exe\"", "timeout": 600 }] }],
    "PostToolUse": [{ "hooks": [{ "type": "command", "command": "\"D:/path/Pult/desktop/PultHook.exe\"", "timeout": 10 }] }],
    "PostToolUseFailure": [{ "hooks": [{ "type": "command", "command": "\"D:/path/Pult/desktop/PultHook.exe\"", "timeout": 10 }] }],
    "StopFailure": [{ "hooks": [{ "type": "command", "command": "\"D:/path/Pult/desktop/PultHook.exe\"", "timeout": 10 }] }],
    "SessionStart": [{ "hooks": [{ "type": "command", "command": "\"D:/path/Pult/desktop/PultHook.exe\"", "timeout": 10 }] }]
  }
}
```

`PultHook.exe` сам отпускает агента через 570 с (`--limit <с>` меняет), это меньше `timeout` 600.

## Как работает

- Хук отправляет событие, только если порт держит `Pult.exe` из его же папки (владелец порта по таблице TCP), и
  прикладывает секрет `%LOCALAPPDATA%\Pult\hook-secret.txt`. Иначе — пустой ответ: агент показывает свой диалог.
  Отказы хука — `%LOCALAPPDATA%\Pult\hook-log.txt`.
- Решение с телефона хук подтверждает (`taken`); только после этого телефону уходит `answered`. Хук не взял решение
  (опоздал) — `expired`, связь с хуком оборвалась — `unknown`.
- `PermissionRequest` в режиме «с телефона» уходит на телефон и ждёт ответа, срока, переключения в «с ПК» или ответа
  на ПК: разрешение — `PostToolUse` того же треда, инструмента и `tool_input`; отказ — запись транскрипта
  (`toolDenialKind: user-rejected` или `[Request interrupted by user for tool use]`).
- `Stop` открывает окно текста, только если поток телефона открыт и продолжений подряд меньше предела
  (`CLAUDE_CODE_STOP_HOOK_BLOCK_CAP`, по умолчанию 8). Окно закрывают текст, срок, закрытие потока телефона, режим
  «с ПК» и запись `enqueue` в транскрипте (ввод на ПК или сообщение другого треда).
- Нет связи с сервером — запрос ждёт её в пределах срока; явный отказ сервера (4xx) — хук отпускается сразу.
- На `hello` телефона desktop шлёт режим и заново открытые запросы (телефон отбрасывает повторы по `request_id`).
- Режим при запуске — «с телефона»; переключатель — в меню трея.
