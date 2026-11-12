# Kotodama · 言霊

**Приложение для изучения японского с нуля** — для Windows, на русском языке.
*Котодама* — «душа слова»: в японской традиции правильно произнесённое слово обретает силу.

## Что внутри

- **Путь обучения** — 19 юнитов от каны до N4: сначала теория, потом практика по шагам.
- **Кана, слова, кандзи** — уровни N5, N4 и N3, около 2 600 слов с примерами фраз, 380 кандзи с анимацией порядка черт, разбором на части и подсказками для запоминания, интервальные повторения по алгоритму **FSRS**.
- **Питч-акцент** — линии высокого и низкого тона у 70 000 слов (например, 箸 / 橋 / 端 — «палочки», «мост», «край»).
- **Тест уровня при первом запуске** — не нужно начинать с азбуки, если ты её уже знаешь.
- **Грамматика** — 45 уроков N5–N3 (включая кэйго) с объяснениями, озвученными примерами и упражнениями.
- **Говорение** — повторяешь за носителем, а приложение оценивает произношение по трём параметрам: звуки (распознавание Whisper, сравнение по звучанию, а не по написанию), интонация (график мелодии голоса) и ритм. Подсказки по типичным ошибкам: っ, долгие гласные, ん.
- **Минимальные пары** — おばさん/おばあさん, きて/きって, かき/かぎ: сначала учишься слышать разницу, потом произносить. Плюс урок «Мои слова вслух» из твоих карточек.
- **Диалоги по ролям** — собеседник говорит, ты отвечаешь вслух.
- **Разговор с ИИ** — свободный диалог на японском с локальной нейросетью (llama.cpp, без интернета): сценарии, ответ голосом, исправления и подсказки.
- **Слово дня** — уведомление с озвучкой в выбранное время; значок с числом карточек к повторению.
- **Чтение** — тексты с фуриганой, озвучкой и словарём по клику.
- **Словарь** — 69 000 статей с русским переводом (JMdict).
- **Тренажёры и игры** — спряжение, счётные суффиксы, диктант, прописи пером, «Кана-дождь», «Найди пару», «Спринт», пробный тест JLPT.
- Латиница под каждой фразой, светлая и тёмная темы, напоминания, автообновление.

## Установка

Скачай архив из раздела [Releases](../../releases/latest):

| Архив | Для кого |
|---|---|
| `Kotodama-Setup-1.2.0.exe` | Обычная установка: ярлык в «Пуске», удаление через «Приложения». Права администратора не нужны. |
| `Kotodama-portable-win-x64.zip` | Распакуй и запусти `Kotodama.exe`. Ничего устанавливать не нужно. |
| `Kotodama-win-x64.zip` | Архив поменьше, но нужен [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). |
| `Kotodama-linux-x64.tar.gz` | **Linux** (x64): распакуй, запусти `./Kotodama` или `./install.sh` — появится в меню приложений. |

Требования: Windows 10 (1809+) или 11, 64-бит; Linux x64 (Ubuntu 22.04+, Fedora, Arch и др.). В Linux для звука нужен PulseAudio или PipeWire (`pulseaudio-utils`), для японского текста — шрифт `fonts-noto-cjk`.

Модель распознавания речи (190 МБ) и офлайн-голоса VOICEVOX скачиваются кнопками внутри приложения.

## Сборка из исходников

Весь код — кроссплатформенный проект [`xplat/`](xplat/) (Avalonia): Windows, Linux и общая часть Android ([`android/`](android/)).

```
dotnet run --project xplat           :: запуск
run-tests.cmd                        :: автотесты (xUnit, tests/Kotodama.XTests)
publish.cmd                          :: сборка для своего ПК → dist
publish-all.cmd                      :: все файлы релиза (Windows, Linux, установщик)
build-installer.cmd                  :: установщик (нужен Inno Setup 6)
```

Linux-архив собирается и проверяется автоматически в GitHub Actions (`.github/workflows/linux.yml`) и прикрепляется к каждому новому релизу.

Скрипты подготовки данных (латиница, словарь, порядок черт, частотные слова) лежат в [`tools/`](tools/README.md).

## Открытые данные и библиотеки

| Что | Источник | Лицензия |
|---|---|---|
| Словарь | [JMdict](https://www.edrdg.org/jmdict/j_jmdict.html) © EDRDG, сборка [jmdict-simplified](https://github.com/scriptin/jmdict-simplified) | CC BY-SA 4.0 |
| Кандзи (чтения) | [KANJIDIC2](https://www.edrdg.org/wiki/index.php/KANJIDIC_Project) © EDRDG | CC BY-SA 4.0 |
| Питч-акцент | [Kanjium](https://github.com/mifunetoshiro/kanjium) | CC BY-SA 4.0 |
| Части кандзи | [KRADFILE](https://www.edrdg.org/krad/kradinf.html) © EDRDG | CC BY-SA 4.0 |
| Порядок черт | [KanjiVG](https://kanjivg.tagaini.net/) © Ulrich Apel | CC BY-SA 3.0 |
| Примеры фраз | [Tatoeba](https://tatoeba.org) | CC BY 2.0 FR |
| Иллюстрации | [Microsoft Fluent Emoji](https://github.com/microsoft/fluentui-emoji) | MIT |
| Распознавание речи | [OpenAI Whisper](https://github.com/openai/whisper) через [Whisper.net](https://github.com/sandrohanea/whisper.net) | MIT |
| Офлайн-голоса | [VOICEVOX](https://voicevox.hiroshiba.jp/) (скачиваются отдельно) | по условиям персонажей; при публикации озвучки укажи «VOICEVOX:имя» |
| Аудио | [NAudio](https://github.com/naudio/NAudio), [NLayer](https://github.com/naudio/NLayer) | MIT |
| Интерфейс | [Avalonia](https://avaloniaui.net/) | MIT |
| Языковая модель | [llama.cpp](https://github.com/ggml-org/llama.cpp) через [LLamaSharp](https://github.com/SciSharp/LLamaSharp); модели Qwen 2.5 (Apache 2.0 / Qwen Research), Gemma 2 JPN (Gemma Terms) — скачиваются отдельно | MIT |
| Иконки (Linux) | [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) © Microsoft | MIT |
