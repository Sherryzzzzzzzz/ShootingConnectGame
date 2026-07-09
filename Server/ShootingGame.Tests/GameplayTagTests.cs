using ShootingGame.Shared.GameplayTags;
using Xunit;

namespace ShootingGame.Tests
{
    public class GameplayTagTests
    {
        public GameplayTagTests()
        {
            GameplayTagConfig.Initialize();
        }

        [Fact]
        public void Register_Tags_HaveUniqueIds()
        {
            Assert.NotEqual(GameplayTagConfig.Id_State, GameplayTagConfig.Id_Action);
            Assert.NotEqual(GameplayTagConfig.Id_State_Dead, GameplayTagConfig.Id_State_Alive);
        }

        [Fact]
        public void SelfMask_IsSingleBit()
        {
            long mask = GameplayTagConfig.Tag_State_Dead.SelfMask;
            Assert.True((mask & (mask - 1)) == 0); // power of 2
        }

        [Fact]
        public void DescendantMask_IncludesSelf()
        {
            long desc = GameplayTagConfig.Tag_State_Dead.DescendantMask;
            long self = GameplayTagConfig.Tag_State_Dead.SelfMask;
            Assert.True((desc & self) != 0);
        }

        [Fact]
        public void Parent_Matches_AllChildren()
        {
            long stateMask = GameplayTagConfig.Tag_State.DescendantMask;

            Assert.True((stateMask & GameplayTagConfig.Tag_State_Dead.SelfMask) != 0,
                "State should match State.Dead");
            Assert.True((stateMask & GameplayTagConfig.Tag_State_Alive.SelfMask) != 0,
                "State should match State.Alive");
            Assert.True((stateMask & GameplayTagConfig.Tag_State_Stunned.SelfMask) != 0,
                "State should match State.Stunned");
            Assert.True((stateMask & GameplayTagConfig.Tag_State_Reloading.SelfMask) != 0,
                "State should match State.Reloading");
        }

        [Fact]
        public void Parent_DoesNotMatch_OtherBranches()
        {
            long stateMask = GameplayTagConfig.Tag_State.DescendantMask;
            Assert.True((stateMask & GameplayTagConfig.Tag_Action_Firing.SelfMask) == 0,
                "State should NOT match Action.Firing");
        }

        [Fact]
        public void Tag_Equals_SameId()
        {
            var t1 = new GameplayTag(GameplayTagConfig.Id_State_Dead);
            var t2 = new GameplayTag(GameplayTagConfig.Id_State_Dead);
            Assert.True(t1 == t2);
            Assert.Equal(t1, t2);
        }

        [Fact]
        public void Tag_NotEquals_DifferentId()
        {
            var t1 = new GameplayTag(GameplayTagConfig.Id_State_Dead);
            var t2 = new GameplayTag(GameplayTagConfig.Id_State_Alive);
            Assert.True(t1 != t2);
        }

        [Fact]
        public void FromName_ReturnsCorrectTag()
        {
            var tag = GameplayTag.FromName("State.Dead");
            Assert.Equal(GameplayTagConfig.Id_State_Dead, tag.Id);
            Assert.True(tag.IsValid);
        }

        [Fact]
        public void FromName_Invalid_ReturnsInvalid()
        {
            var tag = GameplayTag.FromName("NonExistent.Tag");
            Assert.False(tag.IsValid);
        }

        [Fact]
        public void Matches_Exact()
        {
            long mask = GameplayTagConfig.Tag_State_Dead.SelfMask;
            Assert.True(GameplayTagConfig.Tag_State_Dead.MatchesExact(mask));
            Assert.False(GameplayTagConfig.Tag_State_Alive.MatchesExact(mask));
        }

        [Fact]
        public void Matches_Hierarchical()
        {
            long mask = GameplayTagConfig.Tag_State_Dead.SelfMask;
            Assert.True(GameplayTagConfig.Tag_State.Matches(mask));
            Assert.True(GameplayTagConfig.Tag_State_Dead.Matches(mask));
            Assert.False(GameplayTagConfig.Tag_Action.Matches(mask));
        }

        [Fact]
        public void TagContainer_HasTag()
        {
            var container = new TagContainer();
            container.AddTag(GameplayTagConfig.Id_State_Dead);

            Assert.True(container.HasTag(GameplayTagConfig.Tag_State_Dead));
            Assert.True(container.HasTag(GameplayTagConfig.Tag_State)); // hierarchical
        }

        [Fact]
        public void TagContainer_HasAny_HasAll()
        {
            var container = new TagContainer();
            container.AddTag(GameplayTagConfig.Id_State_Dead);
            container.AddTag(GameplayTagConfig.Id_Action_Firing);

            long stateBit = GameplayTagConfig.Tag_State_Dead.SelfMask;
            long actionBit = GameplayTagConfig.Tag_Action_Firing.SelfMask;

            Assert.True(container.HasAny(stateBit | actionBit));
            Assert.True(container.HasAll(stateBit | actionBit));
            Assert.False(container.HasAll(stateBit | actionBit | GameplayTagConfig.Tag_Buff_SpeedBoost.SelfMask));
        }

        [Fact]
        public void TagContainer_RemoveTag()
        {
            var container = new TagContainer();
            container.AddTag(GameplayTagConfig.Id_State_Stunned);
            Assert.True(container.HasTagExact(GameplayTagConfig.Tag_State_Stunned));

            container.RemoveTag(GameplayTagConfig.Id_State_Stunned);
            Assert.False(container.HasTagExact(GameplayTagConfig.Tag_State_Stunned));
        }

        [Fact]
        public void TagContainer_Prediction_Confirm()
        {
            var container = new TagContainer();
            container.PredictAddTag(GameplayTagConfig.Id_Buff_SpeedBoost);
            Assert.False(container.HasTagExact(GameplayTagConfig.Tag_Buff_SpeedBoost));
            Assert.True(container.HasTagPredicted(GameplayTagConfig.Tag_Buff_SpeedBoost));

            container.ConfirmPrediction();
            Assert.True(container.HasTagExact(GameplayTagConfig.Tag_Buff_SpeedBoost));
        }

        [Fact]
        public void TagContainer_Prediction_Reject()
        {
            var container = new TagContainer();
            container.PredictAddTag(GameplayTagConfig.Id_Buff_SpeedBoost);

            container.RejectPrediction();
            Assert.False(container.HasTagPredicted(GameplayTagConfig.Tag_Buff_SpeedBoost));
            Assert.False(container.HasTagExact(GameplayTagConfig.Tag_Buff_SpeedBoost));
        }

        [Fact]
        public void TagContainer_PredictedRemove()
        {
            var container = new TagContainer();
            container.AddTag(GameplayTagConfig.Id_State_Stunned);
            container.PredictRemoveTag(GameplayTagConfig.Id_State_Stunned);

            Assert.True(container.HasTagExact(GameplayTagConfig.Tag_State_Stunned)); // still active
            Assert.False(container.HasTagPredicted(GameplayTagConfig.Tag_State_Stunned)); // predicted removed
        }

        [Fact]
        public void GameplayTagManager_GetMask_ReturnsDescendantMask()
        {
            long mask = GameplayTagManager.GetMask("State");
            Assert.True(mask != 0);
            Assert.True((mask & GameplayTagConfig.Tag_State_Dead.SelfMask) != 0);
        }

        [Fact]
        public void Reset_ClearsAllTags()
        {
            var container = new TagContainer();
            container.AddTag(GameplayTagConfig.Id_State_Dead);
            container.AddTag(GameplayTagConfig.Id_Action_Firing);
            container.PredictAddTag(GameplayTagConfig.Id_Buff_SpeedBoost);

            container.Clear();

            Assert.Equal(0, container.EffectiveMask);
            Assert.Equal(0, container.PredictedAddMask);
            Assert.Equal(0, container.PredictedRemoveMask);
        }
    }
}
