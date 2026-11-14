using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Matsubakira.Services;

/// <summary>
/// Загрузка больших файлов (модели, голоса): потоком на диск во временный .part, докачка прерванной загрузки
/// (HTTP Range), проверка SHA-256 по закреплённой таблице и только потом — переименование в итоговый файл.
/// Недокачанный или подменённый файл никогда не считается установленным.
/// </summary>
public static class Downloader
{
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>SHA-256 закреплённых версий: Whisper (huggingface ggerganov/whisper.cpp), VOICEVOX 0.17.1, ONNX Runtime 1.23.2, словарь Open JTalk 1.11.</summary>
    public static readonly Dictionary<string, string> Sha256 = new()
    {
        ["ggml-large-v3-turbo-q5_0.bin"] = "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
        ["ggml-small-q5_1.bin"] = "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",
        ["ggml-base-q5_1.bin"] = "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898",
        ["voicevox_core-windows-x64-0.17.1.zip"] = "7e6a4f6d39c0a083e694a9191289df3b09725abfa676027228a72c01a2ae565b",
        ["voicevox_core-linux-x64-0.17.1.zip"] = "844b49ce48040ecd85c8b2baaf75f77855277a0f92f6e87ea7ec45c820943323",
        ["voicevox_core-linux-arm64-0.17.1.zip"] = "3a5e9876df26fdece0ddb8de9be09f438b4c884dfd0b58d248b27c91e0e6c807",
        ["voicevox_core-android-arm64-0.17.1.zip"] = "069f29018ea27d939e3198ef34592a489181ba3d0287819f80c518963c639d37",
        ["voicevox_core-android-x86_64-0.17.1.zip"] = "43e8ef75b199491b6e3126be5f4328542b6a8d3a0984c5e98931b696d752cdc2",
        ["voicevox_onnxruntime-win-x64-1.23.2.tgz"] = "1bf9635d1c0ffd4e7345e0f628cecbabde65782c73c5051d52a52e7edf7ce11c",
        ["voicevox_onnxruntime-linux-x64-1.23.2.tgz"] = "0860cfbd5a80201b1bf95cae13ca2db8dc01f32f5674cc8193146047a9212fdb",
        ["voicevox_onnxruntime-linux-arm64-1.23.2.tgz"] = "02cf370ce89c80b3ca7e0d7ab01f2bda89f9325975957e744e0cb684dd364901",
        ["voicevox_onnxruntime-android-arm64-1.23.2.tgz"] = "43e6fc2a89ea6412d4cc20215042a3b34da22fdf05c1bb76256a686f82378d46",
        ["voicevox_onnxruntime-android-x64-1.23.2.tgz"] = "807e2ea50dfb2a309416adb1cee6edc53d3d8eae1d702ad4e6f199a0f91f2d24",
        ["open_jtalk_dic_utf_8-1.11.tar.gz"] = "fe6ba0e43542cef98339abdffd903e062008ea170b04e7e2a35da805902f382a",
        ["0.vvm"] = "ecd35374d4182cd883cba5040376f7f888cc6ba248b1c2f4cea07cdb34bb1318",
        ["1.vvm"] = "e16ffb6fc3e13e1be4ebc3831cc39df019cbac96b28bbed7653a4bb51c4d7d5d",
        ["2.vvm"] = "2eb9a67e3bce09a7f209eb21a56926fa94476352b6ce874c9acd8f465871a4f4",
        ["3.vvm"] = "54aeea4aa51c56e87acc1d406fafc2bd980414c36ebceae0830340b83791d3d7",
        ["4.vvm"] = "716d2900f6b93ba22119af0e236c32fdbf6fea81c519b5d4830afcb2db349522",
        ["5.vvm"] = "67451cdbc3e473dba4d09c878903a656be9e7030cb5901155e5a51634ed8b318",
        ["6.vvm"] = "975d29f225cf605b0e75a0bb66f3f952821b16bbfa49a3a9b96a7688bf21abbe",
        ["7.vvm"] = "d8e3bc97610805901fb7647226a233fe15056f30d1f91d270895c60198625569",
        ["8.vvm"] = "0625c497f1f22be87d03281676c0292d834e262338e70ccaf83af7f34aa83b99",
        ["9.vvm"] = "3dca586c0aa553772419bdc090045f9ea73bd9ed82b88b7dd5737bdd6eddee29",
        ["10.vvm"] = "dea2104b05391fdc37d8c27b4104569d5bff3a52bcb9e4077bb2b1f951c7524a",
        ["11.vvm"] = "3fb0c0f12ec37fda568020509d6e63a0a6b5d6edf3c2330508c6bf4e3eeb8b82",
        ["12.vvm"] = "894464a6cbf59bdf930b8406097f44d2f25bb762db5bc71c9e9c31e1f904c715",
        ["13.vvm"] = "df381d94df92992c4170017d2f1ca386f7a5c3cb559e1bd0578f899910529e29",
        ["14.vvm"] = "63cbabeb29ee9a4837a2462c77fc5cecd380abb5bdbcda4c40b3c34f8f2f9d38",
        ["15.vvm"] = "c29b0c0f8e332fcedc2a5de4b4426feb75fc9f587c314da99af8dbc964287221",
        ["16.vvm"] = "545a2365dbb3f457d2ee9e24b45313968aa4c8351d513f3a79d9922a17569cab",
        ["17.vvm"] = "eb96b6c71fa80f905a86ec7f221f10fbf31f84c0675f919a25d198d8561e3f9a",
        ["18.vvm"] = "33e5177ebbe1ec5829769c1017f1294641dbf22c43cd2af4896ae0738a14ed13",
        ["19.vvm"] = "b9e1c5ddd5b418a6f1d6377d7d00df32c682b580ac9d326675ba4e0b7bb4fc3d",
        ["20.vvm"] = "fcfa6438d71df86c2013a8f285224fb96b632c4f07486d9855474f3df9a50a09",
        ["21.vvm"] = "f6b937e953409a65f3d7609335a4dfd19fdd9458be27d988bdd8296034adfaf6",
        ["22.vvm"] = "1c580f93caaa89f517d2431ee82519eab39c389d1bfb8791035f5c5af71eb17d",
        ["23.vvm"] = "4e09d14a73e5c3e6ec237cf37aae1ce44464e0d4b8a36e7580cb7b475f494dda",
        ["24.vvm"] = "3e62f5ce0ac345ed74c0e3bc9051cd99baaed37389e33f64195c9e0e49f34594",
        ["25.vvm"] = "735e9d62928d3528a31d194d495b860ac2ec20aa978630311b29dc6be0d2de6a",
        ["n0.vvm"] = "f9793559a0ecad1622c15e68674860d7d00ce2736cf02b3f46b083e989c74ec7",
        ["s0.vvm"] = "e97971e3d9c0feaf7a373a5b13b905a885731ce61eef489857055a6631030038",
    };

    /// <summary>
    /// Скачать url в dest. Прерванная загрузка продолжается с места обрыва. Хэш берётся из таблицы по имени файла
    /// в адресе; файла без хэша в таблице не бывает — иначе ошибка (новую версию нужно сначала закрепить).
    /// </summary>
    public static async Task FileAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct, long expectedSize = 0)
    {
        ProgressStore.EnsureNetworkAllowed();
        var name = Path.GetFileName(new Uri(url).AbsolutePath);
        if (!Sha256.TryGetValue(name, out var want)) throw new InvalidDataException($"нет контрольной суммы для {name}");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var part = dest + ".part";
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (expectedSize > 0 && have > expectedSize) { File.Delete(part); have = 0; }
        // скачано целиком, но не проверено (программу закрыли на проверке) — проверяем без запроса
        if (expectedSize > 0 && have == expectedSize && await Verify(part, want, ct)) { File.Move(part, dest, true); progress?.Report(1); return; }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(part); have = 0; resp.Dispose(); await FileAsync(url, dest, progress, ct, expectedSize); return; }
        resp.EnsureSuccessStatusCode();
        bool resumed = have > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resumed) have = 0; // сервер не умеет докачку — начинаем заново
        long total = resp.Content.Headers.ContentLength is { } cl ? cl + have : expectedSize;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
        {
            var buf = new byte[1 << 16];
            long done = have, lastReport = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (done - lastReport > 1 << 20 && total > 0) { progress?.Report((double)done / total); lastReport = done; }
            }
        }
        if (!await Verify(part, want, ct))
        {
            File.Delete(part);
            throw new InvalidDataException($"{name}: файл повреждён или подменён (контрольная сумма не совпала), попробуй ещё раз");
        }
        File.Move(part, dest, true);
        progress?.Report(1);
    }

    public static async Task<bool> Verify(string path, string sha256, CancellationToken ct = default)
    {
        await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}
