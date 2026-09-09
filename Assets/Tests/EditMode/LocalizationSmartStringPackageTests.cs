using DeNelle.Core.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;

namespace DeNelle.Tests.EditMode
{
    public sealed class LocalizationSmartStringPackageTests
    {
        [Test]
        public void HudCollectorsCount_CommittedUnityTable_IsSmartAndFormatsPositionally()
        {
            const string path = "Assets/Localization/Tables/GameStrings_en.asset";
            StringTable table = AssetDatabase.LoadAssetAtPath<StringTable>(path);

            Assert.That(table, Is.Not.Null, "committed English GameStrings table missing");
            Assert.That(table.LocaleIdentifier.Code, Is.EqualTo("en"));

            StringTableEntry entry = table.GetEntry(HudStrings.KeyCollectorsCount);
            Assert.That(entry, Is.Not.Null, "hudCollectorsCount missing from actual Unity StringTable");
            Assert.That(entry.Value, Is.EqualTo("Collectors {0}/{1} full"));
            Assert.That(entry.IsSmart, Is.True,
                "positional entry lost SmartFormatTag; package formatting path is no longer pinned");
            Assert.That(entry.GetLocalizedString(new object[] { 2, 3 }),
                Is.EqualTo("Collectors 2/3 full"));
        }
    }
}
