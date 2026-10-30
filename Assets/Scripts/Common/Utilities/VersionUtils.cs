using System;
using System.Globalization;

namespace Common.Utilities
{
    /// <summary>
    /// 点分版本号比较工具（如 "1.2.10" vs "1.2.9"）。
    /// 用于「最低 App 版本」门禁：按数字段逐段比较，段数不足补 0，
    /// 段内不是纯数字时（如 "1.0.0-beta"）回退为字符串序比较。
    /// </summary>
    public static class VersionUtils
    {
        /// <summary>
        /// 比较两个版本号：a &lt; b 返回 -1，相等返回 0，a &gt; b 返回 1。
        /// null/空串视为最小版本。
        /// </summary>
        public static int Compare(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal))
            {
                return 0;
            }

            if (string.IsNullOrEmpty(a))
            {
                return -1;
            }

            if (string.IsNullOrEmpty(b))
            {
                return 1;
            }

            var sa = a.Split('.');
            var sb = b.Split('.');
            var len = Math.Max(sa.Length, sb.Length);
            for (var i = 0; i < len; i++)
            {
                var xa = i < sa.Length ? sa[i] : "0";
                var xb = i < sb.Length ? sb[i] : "0";
                var c = CompareSegment(xa, xb);
                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 判断 current 是否低于要求的最低版本 minimum。
        /// 任一为空视为不启用门禁，返回 false。
        /// </summary>
        public static bool IsBelow(string current, string minimum)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(minimum))
            {
                return false;
            }

            return Compare(current, minimum) < 0;
        }

        private static int CompareSegment(string xa, string xb)
        {
            var aIsNumber = int.TryParse(xa, NumberStyles.Integer, CultureInfo.InvariantCulture, out var va);
            var bIsNumber = int.TryParse(xb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vb);
            if (aIsNumber && bIsNumber)
            {
                return va.CompareTo(vb);
            }

            return string.CompareOrdinal(xa, xb);
        }
    }
}
