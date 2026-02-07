using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

public class CPHInline
{
    public class Supporter
    {
        public string supporterId { get; set; }
        public string supporterName { get; set; }
    }

    public class SupportersResponse
    {
        public List<Supporter> data { get; set; }
    }

    private Supporter GetSupporter(string authToken, string targetUsername)
    {
        string lowerName = targetUsername.ToLowerInvariant();
        string cacheKey = "supid_" + lowerName;

        string cachedId = CPH.GetGlobalVar<string>(cacheKey);
        if (!string.IsNullOrEmpty(cachedId))
        {
            CPH.LogInfo($"[cache hit] {targetUsername} → используем ID {cachedId}");
            return new Supporter
            {
                supporterId = cachedId,
                supporterName = targetUsername 
            };
        }

        try
        {
            var payload = new
            {
                limit = 1,
                skip = 0,
                query = targetUsername,
                filters = new[] { 0 }
            };

            string payloadJson = JsonConvert.SerializeObject(payload);

            var request = (HttpWebRequest)WebRequest.Create("https://memealerts.com/api/supporters");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", authToken);

            using (var writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(payloadJson);
            }

            string responseText;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                responseText = reader.ReadToEnd();
            }

            var supporters = JsonConvert.DeserializeObject<SupportersResponse>(responseText);

            if (supporters?.data != null && supporters.data.Count > 0)
            {
                var first = supporters.data[0];
                if (string.Equals(first.supporterName?.Trim(), targetUsername.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    string id = first.supporterId;
                    string name = first.supporterName.Trim();

                    CPH.LogInfo($"Найден {name} (ID: {id}) → кэшируем");
                    CPH.SetGlobalVar(cacheKey, id, true);

                    return new Supporter
                    {
                        supporterId = id,
                        supporterName = name
                    };
                }
            }

            CPH.LogWarn($"Поиск по '{targetUsername}' → ничего подходящего не найдено");
            return null;
        }
        catch (WebException wex)
        {
            string err = wex.Message;
            if (wex.Response != null)
            {
                using (var r = new StreamReader(wex.Response.GetResponseStream()))
                    err = r.ReadToEnd();
            }
            CPH.LogError($"Ошибка поиска '{targetUsername}': {err}");
            return null;
        }
        catch (Exception ex)
        {
            CPH.LogError($"Критическая ошибка поиска '{targetUsername}': {ex.Message}");
            return null;
        }
    }

    public bool Execute()
    {
        string targetUsername = args.ContainsKey("rawInput") ? args["rawInput"]?.ToString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(targetUsername))
        {
            CPH.SendMessage("❌ Вы не ввели ник для получения MemeCoins.");
            return true;
        }

        string callerUsername = args.ContainsKey("userName") ? args["userName"]?.ToString()?.Trim() : null;
        string rewardId = args.ContainsKey("rewardId") ? args["rewardId"]?.ToString() : null;
        string redemptionId = args.ContainsKey("redemptionId") ? args["redemptionId"]?.ToString() : null;

        int coins = CPH.GetGlobalVar<int>("coins");
        string streamerId = CPH.GetGlobalVar<string>("streamerId");
        string bearer = CPH.GetGlobalVar<string>("bearer") ?? "";
        string authToken = string.IsNullOrEmpty(bearer) ? "" : "Bearer " + bearer;

        bool success = false;

        try
        {
            var supporter = GetSupporter(authToken, targetUsername);

            if (supporter == null)
            {
                CPH.SendMessage($"Пользователь с ником '{targetUsername}' не найден. Баллы возвращены.");
            }
            else
            {
                var bonusPayload = new
                {
                    userId = supporter.supporterId,
                    streamerId = streamerId,
                    value = coins
                };

                string bonusJson = JsonConvert.SerializeObject(bonusPayload);

                var req = (HttpWebRequest)WebRequest.Create("https://memealerts.com/api/user/give-bonus");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers.Add("Authorization", authToken);
                req.Accept = "application/json";
                req.UserAgent = "Mozilla/5.0";

                using (var writer = new StreamWriter(req.GetRequestStream()))
                {
                    writer.Write(bonusJson);
                }

                using (var response = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    reader.ReadToEnd();
                }

                CPH.SendMessage($"{supporter.supporterName} получил {coins} мемкоинов!");
                CPH.TwitchRedemptionFulfill(rewardId, redemptionId);
                success = true;
            }
        }
        catch (WebException wex)
        {
            string errMsg = wex.Message;
            if (wex.Response != null)
            {
                using (var reader = new StreamReader(wex.Response.GetResponseStream()))
                {
                    errMsg = reader.ReadToEnd();
                }
            }
            CPH.LogError($"Ошибка выдачи: {errMsg}");
            CPH.SendMessage($"❌ Не удалось выдать бонус. {errMsg}");
        }
        catch (Exception ex)
        {
            CPH.LogError($"Общая ошибка: {ex}");
            CPH.SendMessage($"❌ Ошибка: {ex.Message}");
        }
        finally
        {
            if (!success && !string.IsNullOrEmpty(rewardId) && !string.IsNullOrEmpty(redemptionId) && !string.IsNullOrEmpty(callerUsername))
            {
                try
                {
                    bool canceled = CPH.TwitchRedemptionCancel(rewardId, redemptionId);
                    CPH.LogInfo(canceled ? $"Заявка {redemptionId} отмена" : $"Не удалось отменить заявку");
                }
                catch (Exception ex)
                {
                    CPH.LogError($"Ошибка отмены: {ex}");
                }
            }
        }

        return true;
    }
}
