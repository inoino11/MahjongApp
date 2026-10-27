using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.JSInterop;

namespace MahjongApp.Services;

/// <summary>
/// 問い合わせ・エラー報告を、端末で運営者の公開鍵により暗号化して Worker に送る（wwwroot/js/feedback.js）
/// </summary>
public class FeedbackSender
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;

    public FeedbackSender(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    private class WorkerError
    {
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// 送信する。成功なら null、失敗なら利用者向けの理由を返す
    /// </summary>
    public async Task<string?> SendAsync(string category, string message, string? contact, bool includeEnvironment, string honeypot = "")
    {
        try
        {
            if (!await _js.InvokeAsync<bool>("feedbackCrypto.isReady")) return "問い合わせの受付を準備中です";
            string? environment = null;
            if (includeEnvironment)
            {
                string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
                environment = $"{version} / {await _js.InvokeAsync<string>("feedbackCrypto.getEnvironment")}";
            }
            string plain = JsonSerializer.Serialize(new
            {
                category,
                message = message.Trim(),
                contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim(),
                environment
            });
            string encrypted = await _js.InvokeAsync<string>("feedbackCrypto.encrypt", plain);
            var response = await _http.PostAsJsonAsync($"{LiveShareService.WorkerUrl}/feedback", new { encrypted, website = honeypot });
            if (response.IsSuccessStatusCode) return null;

            string error = "";
            try { error = (await response.Content.ReadFromJsonAsync<WorkerError>())?.Error ?? ""; } catch { }
            return response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests when error == "daily limit" => WorkerErrors.DailyLimitMessage,
                HttpStatusCode.TooManyRequests => "続けて送信されたため受け付けませんでした\n1 分ほど待ってから送ってください",
                HttpStatusCode.RequestEntityTooLarge => "内容が長すぎます",
                _ => $"送信に失敗しました（{(int)response.StatusCode}）"
            };
        }
        catch (Exception ex)
        {
            return $"送信に失敗しました。通信状況を確認してください\n{ex.Message}";
        }
    }
}
