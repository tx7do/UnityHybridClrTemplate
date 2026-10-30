using Common.Utilities;
using NUnit.Framework;

namespace EditorTests
{
    /// <summary>
    /// 版本号比较与「最低 App 版本」门禁的语义测试。
    /// </summary>
    public class VersionUtilsTests
    {
        [Test]
        public void Compare_NumericSegments_NotLexicographic()
        {
            // 关键用例：字符串比较会得到 "9" > "10"，必须按数字比较
            Assert.AreEqual(-1, VersionUtils.Compare("1.2.9", "1.2.10"));
            Assert.AreEqual(1, VersionUtils.Compare("1.10.0", "1.9.0"));
        }

        [Test]
        public void Compare_Equal_And_PaddedSegments()
        {
            Assert.AreEqual(0, VersionUtils.Compare("1.2.3", "1.2.3"));
            Assert.AreEqual(0, VersionUtils.Compare("1.2", "1.2.0.0"));
        }

        [Test]
        public void Compare_MissingSegments_TreatedAsZero()
        {
            Assert.AreEqual(1, VersionUtils.Compare("1.0.0.1", "1.0.0"));
            Assert.AreEqual(-1, VersionUtils.Compare("1.0", "1.0.1"));
        }

        [Test]
        public void Compare_NullOrEmpty_IsSmallest()
        {
            Assert.AreEqual(-1, VersionUtils.Compare(null, "1.0"));
            Assert.AreEqual(-1, VersionUtils.Compare("", "0.0.1"));
            Assert.AreEqual(1, VersionUtils.Compare("1.0", null));
            Assert.AreEqual(0, VersionUtils.Compare(null, null));
        }

        [Test]
        public void Compare_NonNumericSegment_FallsBackToOrdinal()
        {
            // 段内非数字（预发布后缀等）回退为字符串比较，锁定该语义避免隐式变化
            Assert.Greater(VersionUtils.Compare("1.0.0-beta", "1.0.0"), 0);
            Assert.Less(VersionUtils.Compare("1.0.0-alpha", "1.0.0-beta"), 0);
        }

        [Test]
        public void IsBelow_GatesOldClientsOnly()
        {
            Assert.IsTrue(VersionUtils.IsBelow("1.0.0", "1.2.0"));
            Assert.IsTrue(VersionUtils.IsBelow("1.1.9", "1.2.0"));
            Assert.IsFalse(VersionUtils.IsBelow("1.2.0", "1.2.0"));
            Assert.IsFalse(VersionUtils.IsBelow("2.0.0", "1.9.9"));
        }

        [Test]
        public void IsBelow_EmptyValues_DisablesGate()
        {
            Assert.IsFalse(VersionUtils.IsBelow("1.0.0", null));
            Assert.IsFalse(VersionUtils.IsBelow("1.0.0", ""));
            Assert.IsFalse(VersionUtils.IsBelow(null, "1.2.0"));
        }
    }
}
