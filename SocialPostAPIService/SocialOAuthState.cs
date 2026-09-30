using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public static class SocialOAuthState
    {
        public static string Create(string organizationID, string secret, string platform = SocialPlatform.TikTok)
        {
            var payload = new JObject
            {
                ["organization_id"] = organizationID,
                ["platform"] = platform,
                ["expires"] = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
                ["nonce"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
            };
            string encoded = Encode(Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None)));
            return encoded + "." + Encode(Sign(encoded, secret));
        }

        public static void Validate(string state, string organizationID, string secret, string platform = SocialPlatform.TikTok)
        {
            try
            {
                string[] parts = state.Split('.');
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(organizationID) ||
                    !CryptographicOperations.FixedTimeEquals(Decode(parts[1]), Sign(parts[0], secret)))
                    throw new FormatException();
                var payload = JObject.Parse(Encoding.UTF8.GetString(Decode(parts[0])));
                if (payload.Value<string>("organization_id") != organizationID ||
                    payload.Value<string>("platform") != platform ||
                    payload.Value<long>("expires") <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    throw new FormatException();
            }
            catch
            {
                throw ResponseStatusFactory.BadRequest("Social authorization state is invalid or expired. Please reconnect.");
            }
        }

        private static byte[] Sign(string value, string secret) =>
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(value));

        // The backend derives the verifier; it is never embedded in public state.
        public static string XCodeVerifier(string state, string secret) => Encode(Sign("x-pkce:" + state, secret));

        public static string XCodeChallenge(string verifier) => Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        private static string Encode(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Decode(string value) =>
            Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    }
}
