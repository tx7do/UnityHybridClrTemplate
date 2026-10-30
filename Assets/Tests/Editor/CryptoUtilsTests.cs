using System.IO;
using System.Text;
using Common.Utilities;
using NUnit.Framework;
using UnityEngine;

namespace EditorTests
{
    /// <summary>
    /// MD5 工具测试：热更 DLL 下载校验依赖这些方法的输出格式（32 位小写 hex）。
    /// </summary>
    public class CryptoUtilsTests
    {
        private const string HelloMd5 = "5d41402abc4b2a76b9719d911017c592";
        private const string EmptyMd5 = "d41d8cd98f00b204e9800998ecf8427e";

        [Test]
        public void MD5Bytes_Hello_KnownValue()
        {
            Assert.AreEqual(HelloMd5, CryptoUtils.MD5Bytes(Encoding.UTF8.GetBytes("hello")));
        }

        [Test]
        public void MD5Bytes_Empty_KnownValue()
        {
            Assert.AreEqual(EmptyMd5, CryptoUtils.MD5Bytes(new byte[0]));
        }

        [Test]
        public void MD5_String_MatchesBytes()
        {
            Assert.AreEqual(HelloMd5, CryptoUtils.MD5("hello"));
        }

        [Test]
        public void MD5File_MatchesMd5Bytes()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hybridclr_md5_test_{Path.GetRandomFileName()}");
            try
            {
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes("hello"));
                Assert.AreEqual(HelloMd5, CryptoUtils.MD5File(path));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
