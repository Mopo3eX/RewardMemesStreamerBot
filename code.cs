using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
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

private List<Supporter> GetAllSupporters(string authToken)
{
    List<Supporter> allSupporters = new List<Supporter>();
    int limit = 1000;
    int skip = 0;

    while (true)
    {
        var requestPayload = new { limit = limit, skip = skip };
        string payloadJson = JsonConvert.SerializeObject(requestPayload);

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

        SupportersResponse supporters = JsonConvert.DeserializeObject<SupportersResponse>(responseText);

        if (supporters?.data == null || supporters.data.Count == 0)
        {
            CPH.LogInfo($"skip={skip}: пусто → конец.");
            break;
        }

        CPH.LogInfo($"skip={skip}: получено {supporters.data.Count} записей");
        allSupporters.AddRange(supporters.data);

        if (supporters.data.Count < limit)
            break; 

        skip += limit;
    }

    CPH.LogInfo($"Всего саппортёров: {allSupporters.Count}");
    return allSupporters;
}



    public bool Execute()
    {

        string targetUsername = args.ContainsKey("rawInput") ? args["rawInput"].ToString().Trim() : null;
        if (string.IsNullOrWhiteSpace(targetUsername))
        {
            CPH.SendMessage("❌ Вы не ввели ник для получения MemeCoins.");
            return true;
        }


        string callerUsername = args.ContainsKey("userName") ? args["userName"].ToString().Trim() : null;


        string rewardId = args.ContainsKey("rewardId") ? args["rewardId"].ToString() : null;
        string redemptionId = args.ContainsKey("redemptionId") ? args["redemptionId"].ToString() : null;

        int coins = CPH.GetGlobalVar<int>("coins");
        string streamerId = CPH.GetGlobalVar<string>("streamerId");

        string authToken = "Bearer " + CPH.GetGlobalVar<string>("bearer") ?? "";
        bool bonusIssuedSuccessfully = false;

        try
        {

            List<Supporter> allSupporters = GetAllSupporters(authToken);

            string supporterId = null;
            string supporterName = null;

            foreach (var supporter in allSupporters)
            {
                if (string.Equals(supporter.supporterName.Trim(), targetUsername, StringComparison.OrdinalIgnoreCase))
                {
                    supporterId = supporter.supporterId;
                    supporterName = supporter.supporterName.Trim();
                    break;
                }
            }

            string bonusRecipientName = supporterId != null ? supporterName : targetUsername;

            if (supporterId == null)
            {
                CPH.SendMessage($"Пользователь с ником '{targetUsername}' не найден. Баллы возвращены.");
            }


            if (supporterId != null)
            {
                var bonusPayload = new
                {
                    userId = supporterId,
                    streamerId = streamerId,
                    value = coins
                };
                string bonusJson = JsonConvert.SerializeObject(bonusPayload);

                var request = (HttpWebRequest)WebRequest.Create("https://memealerts.com/api/user/give-bonus");
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Headers.Add("Authorization", authToken);
                request.Accept = "application/json";
                request.UserAgent = "Mozilla/5.0";

                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    streamWriter.Write(bonusJson);
                    streamWriter.Flush();
                }

                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string result = reader.ReadToEnd();
                        CPH.SendMessage($"{bonusRecipientName} получил {coins} мемкоинов!");
                        CPH.TwitchRedemptionFulfill(rewardId, redemptionId);
                        bonusIssuedSuccessfully = true;
                    }
                }
                catch (WebException wex)
                {
                    bonusIssuedSuccessfully = false;
                    string errorMessage = "";
                    if (wex.Response != null)
                    {
                        using (var reader = new StreamReader(wex.Response.GetResponseStream()))
                        {
                            errorMessage = reader.ReadToEnd();
                            CPH.LogError($"WebException при выдаче MemeCoins: {errorMessage}");
                        }
                    }
                    else
                    {
                        errorMessage = wex.Message;
                        CPH.LogError($"WebException при выдаче MemeCoins: {errorMessage}");
                    }
                    CPH.SendMessage($"❌ Не удалось выдать бонус {bonusRecipientName}. {errorMessage}");
                }
            }


            CPH.LogInfo("=== Диагностика перед возвратом баллов ===");
            CPH.LogInfo($"rewardId: {(rewardId ?? "null")}");
            CPH.LogInfo($"redemptionId: {(redemptionId ?? "null")}");
            CPH.LogInfo($"callerUsername: {(callerUsername ?? "null")}");
            CPH.LogInfo($"bonusIssuedSuccessfully: {bonusIssuedSuccessfully}");


            if (!bonusIssuedSuccessfully && !string.IsNullOrEmpty(rewardId) && !string.IsNullOrEmpty(redemptionId) && !string.IsNullOrEmpty(callerUsername))
            {
                try
                {
                    CPH.LogInfo("Попытка отменить редемпшен через Broadcaster Account...");
                    bool canceled = CPH.TwitchRedemptionCancel(rewardId, redemptionId);

                    if (canceled)
                    {
                        CPH.LogInfo($"Redemption {redemptionId} успешно отменён. Статус CANCELED.");
                    }
                    else
                    {
                        CPH.SendMessage($"Не удалось вернуть баллы Twitch для {callerUsername}.");
                        CPH.LogWarn($"TwitchRedemptionCancel вернул false. rewardId={rewardId}, redemptionId={redemptionId}, user={callerUsername}");
                    }
                }
                catch (Exception ex)
                {
                    CPH.SendMessage($"Ошибка при возврате баллов Twitch: {ex.Message}");
                    CPH.LogError($"Ошибка при вызове TwitchRedemptionCancel: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            CPH.SendMessage($"Исключение: {ex.Message}");
            CPH.LogError($"Общее исключение: {ex}");
        }

        return true;
    }
}
