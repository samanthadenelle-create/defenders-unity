using System;
using DeNelle.Editor.Regression;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class StoneReleaseReferenceProof
    {
        public static void RunBatch()
        {
            StoneSaveMigrationProof.RunIntegrated();
            RaidLootAuthorityProof.RunBatch();
            if (!CollectorIncomeRegression.Run(out string income)) throw new Exception(income);
            if (!CollectorOverflowRegression.Run(out string overflow)) throw new Exception(overflow);
            if (!KillRewardRaidSuppressionRegression.Run(out string raid)) throw new Exception(raid);
            if (!RetiredVocabularyRegression.Run(out string words)) throw new Exception(words);
            Debug.Log("STONE_RELEASE_REFERENCES_OK save/economy migration, raid payout, collector overflow, suppression and retired copy");
        }
    }
}
