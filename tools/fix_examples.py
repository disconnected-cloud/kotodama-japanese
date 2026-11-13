# Замена примеров, подобранных к омониму (この訳 — «перевод», а не わけ «причина» и т.п.)
# python -X utf8 tools/fix_examples.py <проект>
import json, os, sys
proj = sys.argv[1]
FIX = {  # слово: (пример, перевод)
    "わけ": ("そういうわけで、遅れました。", "Вот почему я опоздал."),
    "喜び": ("合格の喜びは大きかった。", "Радость от сдачи экзамена была огромной."),
    "とも": ("二人とも学生です。", "Они оба студенты."),
    "持つ": ("傘を持っていますか。", "У тебя есть зонт?"),
    "打つ": ("彼はボールを強く打った。", "Он сильно ударил по мячу."),
    "登る": ("週末に山に登りました。", "На выходных я поднялся на гору."),
    "玉": ("子供が玉で遊んでいる。", "Ребёнок играет с мячиком."),
    "収める": ("彼は大きな成功を収めた。", "Он добился большого успеха."),
    "勤める": ("姉は銀行に勤めています。", "Моя старшая сестра работает в банке."),
    "ほる": ("庭に穴をほった。", "Я выкопал яму в саду."),
}
enc = lambda x: json.dumps(x, ensure_ascii=False)
n = 0
for f in ["vocab.json", "vocab_n4.json", "vocab_plus.json", "vocab_n3.json", "vocab_n2.json", "vocab_n1.json"]:
    p = os.path.join(proj, "Data", f)
    raw = open(p, encoding="utf8").read()
    for words in json.loads(raw).values():
        for w in words:
            if w[0] in FIX and len(w) > 4 and w[3] and w[3] != FIX[w[0]][0]:
                raw = raw.replace(enc(w[3]), enc(FIX[w[0]][0]), 1).replace(enc(w[4]), enc(FIX[w[0]][1]), 1)
                n += 1
    open(p, "w", encoding="utf8").write(raw)
print("заменено примеров:", n)
