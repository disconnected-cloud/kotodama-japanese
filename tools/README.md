# Подготовка данных

Скрипты запускаются один раз при изменении учебных материалов; результат кладётся в `Data/` и встраивается в exe.

Всё одной командой (Windows и Linux) — источники скачиваются закреплённых версий с проверкой SHA-256 (`tools/sources.json`):

```
python -m venv .venv
.venv/bin/pip install -r tools/requirements.txt          # Windows: .venv\Scripts\pip
.venv/bin/python -X utf8 tools/build_all.py . ../kotodama-cache
```

Порядок шагов и что они делают:

| Шаг | Скрипт | Результат |
|---|---|---|
| dict | `build_data.py <проект> <jmdict-rus.json>` | `Data/dict.tsv.gz`, `Data/romaji.json` (MeCab + UniDic) |
| accents | `build_accents.py <проект> <accents.txt>` | `Data/accents.tsv.gz` (Kanjium) |
| mnemonics | `build_mnemonics.py <проект> <kradfile.gz>` | `Data/mnemonics.json` |
| strokes | `strokes.py <проект> <кэш svg>` | `Data/strokes.json` (KanjiVG) |
| texts | `build_texts.py <проект>` | токены текстов читалки в `Data/texts.json` |
| romaji | `update_romaji.py <проект>` | дополняет `Data/romaji.json` без полной пересборки |
| glosses | `clean_glosses.py`, `fix_examples.py`, `fix_n1.py` | чистка переводов и примеров (повторный запуск безопасен) |
| words | `build_words.py <проект> <tatoeba> <jmdict-rus.json>` | новые слова из корпуса — меняет курс, только явно |

Отдельно и по необходимости: `build_kanji_n3.py` (кандзи N3 из KANJIDIC2), `get_emoji.py` (иллюстрации Fluent Emoji),
`make_icon.py`, `make_icon_font.py`, `subset_jp_font.py` (шрифты и иконки), `vvm_catalog.py` (каталог голосов VOICEVOX),
`pack_linux.py` (архив Linux), `snapdiff.py` (сравнение снимков экрана в CI).

**ID уроков** — поле `"id"` у каждого урока в `grammar*.json`, `speech.json`, `dialogs.json`, `course.json`. Новый урок получает
следующий свободный номер; номера существующих не меняются (на них держится прогресс). **Исправил написание слова или фразы** —
добавь строку в `Data/id_aliases.json` («старый ID → новый»), иначе тест `CardIdTests` не пройдёт.

Лицензии данных: JMdict — CC BY-SA 4.0 (EDRDG), KanjiVG — CC BY-SA 3.0.

## Почему в коде «Matsubakira», а приложение — Kotodama

Первое название проекта — Matsubakira. При переименовании в Kotodama (v0.5) поменялись только видимые имена: exe, окно, установщик, иконка.
Специально остались старыми:

- пространство имён `Matsubakira.*` — переименование задело бы каждый файл и сломало бы историю `git blame`;
- папка данных `%AppData%\Matsubakira` (Linux: `~/.config/Matsubakira`) — иначе у пользователей пропал бы прогресс после обновления.

Если когда-нибудь переименовывать папку данных — только с переносом старой при первом запуске.
