using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace AzureToAWS.Common.Utilities
{
    public class Aes256CbcEncrypter
    {
        private static readonly Encoding encoding = Encoding.UTF8;
        private static readonly string Key = "8UHjPgXZzXCGkhxV2QCnooyJexUzvJrO";

        //public static string Encrypt(string plainText)
        //{
        //    try
        //    {
        //        RijndaelManaged aes = new RijndaelManaged
        //        {
        //            KeySize = 256,
        //            BlockSize = 128,
        //            Padding = PaddingMode.PKCS7,
        //            Mode = CipherMode.CBC,

        //            Key = encoding.GetBytes(Key)
        //        };
        //        aes.GenerateIV();

        //        ICryptoTransform AESEncrypt = aes.CreateEncryptor(aes.Key, aes.IV);
        //        byte[] buffer = encoding.GetBytes(plainText);

        //        string encryptedText = Convert.ToBase64String(AESEncrypt.TransformFinalBlock(buffer, 0, buffer.Length));

        //        string mac = "";

        //        mac = BitConverter.ToString(HmacSHA256(Convert.ToBase64String(aes.IV) + encryptedText, Key)).Replace("-", "").ToLower();

        //        var keyValues = new Dictionary<string, object>
        //        {
        //            { "iv", Convert.ToBase64String(aes.IV) },
        //            { "value", encryptedText },
        //            { "mac", mac },
        //        };

        //        //JavaScriptSerializer serializer = new JavaScriptSerializer();
        //        //return Convert.ToBase64String(encoding.GetBytes(serializer.Serialize(keyValues)));
        //        return Convert.ToBase64String(encoding.GetBytes(JsonSerializer.Serialize(keyValues)));
        //    }
        //    catch (Exception e)
        //    {
        //        throw new Exception("Error encrypting: " + e.Message);
        //    }
        //}

        //public static string Decrypt(string plainText)
        //{
        //    try
        //    {
        //        RijndaelManaged aes = new RijndaelManaged
        //        {
        //            KeySize = 256,
        //            BlockSize = 128,
        //            Padding = PaddingMode.PKCS7,
        //            Mode = CipherMode.CBC,
        //            Key = encoding.GetBytes(Key)
        //        };

        //        // Base 64 decode
        //        byte[] base64Decoded = Convert.FromBase64String(plainText);
        //        string base64DecodedStr = encoding.GetString(base64Decoded);

        //        // JSON Decode base64Str
        //        //JavaScriptSerializer ser = new JavaScriptSerializer();
        //        //var payload = ser.Deserialize<Dictionary<string, string>>(base64DecodedStr);
        //        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(base64DecodedStr);

        //        aes.IV = Convert.FromBase64String(payload["iv"]);

        //        ICryptoTransform AESDecrypt = aes.CreateDecryptor(aes.Key, aes.IV);
        //        byte[] buffer = Convert.FromBase64String(payload["value"]);

        //        return encoding.GetString(AESDecrypt.TransformFinalBlock(buffer, 0, buffer.Length));
        //    }
        //    catch (Exception e)
        //    {
        //        throw new Exception("Error decrypting: " + e.Message);
        //    }
        //}

        public static string Encrypt(string plainText)
        {
            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;
                aes.Key = encoding.GetBytes(Key);
                aes.GenerateIV();

                ICryptoTransform AESEncrypt = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] buffer = encoding.GetBytes(plainText);

                string encryptedText = Convert.ToBase64String(AESEncrypt.TransformFinalBlock(buffer, 0, buffer.Length));

                string mac = BitConverter.ToString(HmacSHA256(Convert.ToBase64String(aes.IV) + encryptedText, Key)).Replace("-", "").ToLower();

                var keyValues = new Dictionary<string, object>
                {
                    { "iv", Convert.ToBase64String(aes.IV) },
                    { "value", encryptedText },
                    { "mac", mac },
                };

                return Convert.ToBase64String(encoding.GetBytes(JsonSerializer.Serialize(keyValues)));
            }
            catch (Exception e)
            {
                throw new Exception("Error encrypting: " + e.Message);
            }
        }

        public static string Decrypt(string plainText)
        {
            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;
                aes.Key = encoding.GetBytes(Key);

                // Base 64 decode
                byte[] base64Decoded = Convert.FromBase64String(plainText);
                string base64DecodedStr = encoding.GetString(base64Decoded);

                // JSON Decode base64Str
                var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(base64DecodedStr);

                aes.IV = Convert.FromBase64String(payload["iv"]);

                ICryptoTransform AESDecrypt = aes.CreateDecryptor(aes.Key, aes.IV);
                byte[] buffer = Convert.FromBase64String(payload["value"]);

                return encoding.GetString(AESDecrypt.TransformFinalBlock(buffer, 0, buffer.Length));
            }
            catch (Exception e)
            {
                throw new Exception("Error decrypting: " + e.Message);
            }
        }

        static byte[] HmacSHA256(string data, string key)
        {
            using HMACSHA256 hmac = new(encoding.GetBytes(key));
            return hmac.ComputeHash(encoding.GetBytes(data));
        }
    }
}