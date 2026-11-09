# Подготовка данных

Скрипты запускаются один раз при изменении учебных материалов; результат кладётся в `Data/` и встраивается в exe.

- `build_data.py` — латиница для предложений (`Data/romaji.json`, разбор MeCab + UniDic) и большой словарь (`Data/dict.tsv.gz` из JMdict).
  ```
  python -m venv .venv && .venv\Scripts\pip install fugashi unidic-lite
  .venv\Scripts\python -X utf8 tools\build_data.py . путь\к\jmdict-rus-*.json
  ```
  JMdict (рус.): https://github.com/scriptin/jmdict-simplified/releases — файл `jmdict-rus-*.json`.
- `strokes.py` — порядок черт из KanjiVG для всех кандзи из `Data/kanji*.json` → `Data/strokes.json`.
  ```
  python -X utf8 tools\strokes.py . папка_для_кэша_svg
  ```

Лицензии данных: JMdict — CC BY-SA 4.0 (EDRDG), KanjiVG — CC BY-SA 3.0.
