using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CopyAzureToAWS.Common.Utilities
{
    public class EncryptionHelper
    {
        private static string key = "!($&IX*#*#&>?(^%";

        public string ThreadName
        {
            get { return Thread.CurrentThread.ManagedThreadId.ToString(); }
        }

        public EncryptionHelper(string EncryptionKey, int BlockSize)
        {
        }

        //public static string Encrypt(string plainText)
        //{
        //    byte[] keyArray;
        //    byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(plainText);

        //    keyArray = UTF8Encoding.UTF8.GetBytes(key);

        //    TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider
        //    {
        //        Key = keyArray,
        //        Mode = CipherMode.ECB,
        //        Padding = PaddingMode.PKCS7
        //    };

        //    ICryptoTransform cTransform = tdes.CreateEncryptor();
        //    byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
        //    tdes.Clear();
        //    return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        //}

        //public static string Encrypt(string plainText)
        //{
        //    byte[] keyArray = Encoding.UTF8.GetBytes(key);
        //    byte[] toEncryptArray = Encoding.UTF8.GetBytes(plainText);

        //    using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
        //    {
        //        aes.Key = keyArray;
        //        aes.Mode = CipherMode.ECB;
        //        aes.Padding = PaddingMode.PKCS7;

        //        ICryptoTransform cTransform = aes.CreateEncryptor();
        //        byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
        //        return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        //    }
        //}

        public static string Encrypt(string plainText)
        {
            byte[] keyArray = Encoding.UTF8.GetBytes(key);
            byte[] toEncryptArray = Encoding.UTF8.GetBytes(plainText);

            using Aes aes = Aes.Create();
            aes.Key = keyArray;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = aes.CreateEncryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        }

        /// <summary>
        /// DeCrypt a string using dual encryption method. Return a DeCrypted clear string
        /// </summary>
        /// <param name="cipherString">encrypted string</param>

        //public static string Decrypt(string cipherText)
        //{
        //    byte[] keyArray;
        //    byte[] toEncryptArray = Convert.FromBase64String(cipherText);

        //    keyArray = UTF8Encoding.UTF8.GetBytes(key);

        //    TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider
        //    {
        //        Key = keyArray,
        //        Mode = CipherMode.ECB,
        //        Padding = PaddingMode.PKCS7
        //    };

        //    ICryptoTransform cTransform = tdes.CreateDecryptor();
        //    byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

        //    tdes.Clear();
        //    return UTF8Encoding.UTF8.GetString(resultArray);
        //}

        public static string Decrypt(string cipherText)
        {
            byte[] keyArray = Encoding.UTF8.GetBytes(key);
            byte[] toEncryptArray = Convert.FromBase64String(cipherText);

            using Aes aes = Aes.Create();
            aes.Key = keyArray;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = aes.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            return Encoding.UTF8.GetString(resultArray);
        }

        public void CompareMD5Hash(Stream SourceStream, Stream DestinationStream, out bool bReturnValue, out string hashSource, out string hashDestination, bool Azure = false)
        {
            bReturnValue = false;
            hashSource = string.Empty;
            hashDestination = string.Empty;

            if (Azure)
            {
                //Because of Encryption BlockSize, after decypting source and decypted hash will not match beause of
                //of extra bytes created during encyption process. so read that many bytes from source and compare to 
                //destination hash.

                //SourceStream      : Encrypted Stream
                //DestinationStream : File from local

                byte[] bytSourceArr = ((MemoryStream)SourceStream).ToArray();
                byte[] bytDestinationArr = ((MemoryStream)DestinationStream).ToArray();

                using MemoryStream SourceStreamTemp = new MemoryStream();
                SourceStreamTemp.Write(bytSourceArr, 0, bytDestinationArr.Length);
                SourceStreamTemp.Position = 0;

                // Hash the input.
                // if we compare hash between SourceStream & DestinationStream, it always fails because of Blocksizeing of Encryption
                //string hashSource = GetMd5Hash(md5, SourceStream);
                hashSource = GetMd5Hash(SourceStreamTemp);
                hashDestination = GetMd5Hash(DestinationStream);
            }
            else
            {
                hashSource = MD5Hash(SourceStream);
                hashDestination = MD5Hash(DestinationStream);
            }

            StringComparer comparer = StringComparer.OrdinalIgnoreCase;
            bReturnValue = (0 == comparer.Compare(hashSource, hashDestination));
        }

        public string MD5Hash(Stream stream)
        {
            string hash = string.Empty;
            using (var md5 = MD5.Create())
            {
                var bytArr = md5.ComputeHash(stream);
                hash = Convert.ToBase64String(bytArr);
            }

            return hash;
        }


        public string GetMd5Hash(Stream inputStream)
        {
            MD5 md5Hash = new MD5CryptoServiceProvider();

            // Convert the input stream to a byte array and compute the hash. 
            byte[] data = md5Hash.ComputeHash(inputStream);

            // Create a new Stringbuilder to collect the bytes 
            // and create a string.
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data  
            // and format each one as a hexadecimal string. 
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string. 
            return sBuilder.ToString();
        }
    }
}
