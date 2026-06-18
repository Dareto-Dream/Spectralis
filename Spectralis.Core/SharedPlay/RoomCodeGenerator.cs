using System;
using System.Security.Cryptography;
using System.Text;

namespace Spectralis.Core.SharedPlay
{
    public static class RoomCodeGenerator
    {
        private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string Generate(int length = 6)
        {
            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            var sb = new StringBuilder(length);
            foreach (var b in bytes)
                sb.Append(Chars[b % Chars.Length]);
            return sb.ToString();
        }

        public static bool IsValid(string code) =>
            code.Length >= 4 && code.Length <= 10 &&
            code.ToUpperInvariant().ToCharArray().All(c => Chars.Contains(c));
    }
}
