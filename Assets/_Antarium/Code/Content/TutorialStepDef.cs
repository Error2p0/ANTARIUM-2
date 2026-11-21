using UnityEngine;

namespace Antarium.Content
{
    /// <summary>What finishes a tutorial step (checked a few times a second against the colony and the player's input).</summary>
    public enum TutorialGoal : byte
    {
        /// <summary>A message only: the player clicks Continue.</summary>
        None,
        /// <summary>Pan the view <see cref="TutorialStepDef.amount"/> cells and zoom once.</summary>
        LookAround,
        /// <summary>Dig out <see cref="TutorialStepDef.amount"/> cells with the Dig tool.</summary>
        DigYourself,
        /// <summary>Have <see cref="TutorialStepDef.amount"/> cells ordered for mining at once.</summary>
        MiningOrders,
        /// <summary>Have <see cref="TutorialStepDef.amount"/> storage zone cells.</summary>
        StorageZone,
        /// <summary><see cref="TutorialStepDef.amount"/> items delivered into the stock since the step began.</summary>
        ItemsStored,
        /// <summary>Open the Queen panel.</summary>
        OpenQueen,
        /// <summary><see cref="TutorialStepDef.amount"/> contracts claimed in this colony.</summary>
        ClaimContract,
        /// <summary><see cref="TutorialStepDef.amount"/> research nodes done in this colony.</summary>
        Research,
        /// <summary>Place <see cref="TutorialStepDef.building"/> (blueprint or finished; any building when empty).</summary>
        PlaceBuilding,
        /// <summary>Open the help (F1).</summary>
        OpenHelp,
    }

    /// <summary>The part of the HUD a tutorial step points at (it blinks while the step is on).</summary>
    public enum TutorialHighlight : byte
    {
        None, DigTool, MineTool, StorageTool, BuildTool, QueenButton, ResearchButton, Contracts, Resources,
    }

    /// <summary>
    /// One step of the first-colony tutorial. The title is the display name; the body may name keys with placeholders
    /// such as <c>{Dig}</c> (see <c>HelpText</c>) and <c>{Cost}</c> (the cost of <see cref="building"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Tutorial Step", fileName = "TutorialStep")]
    public sealed class TutorialStepDef : ContentDef
    {
        [Tooltip("Position in the tutorial (lower first).")]
        public int order;
        [TextArea(4, 12)] public string body;
        public TutorialGoal goal;
        [Tooltip("How much of the goal is needed (cells, items, contracts...). Ignored by goals that only need doing once.")]
        [Min(1)] public int amount = 1;
        [Tooltip("PlaceBuilding: the building to place.")]
        public BuildingDef building;
        public TutorialHighlight highlight;
        [Tooltip("Help page offered with the step (More help). Optional.")]
        public HelpTopicDef helpTopic;
    }
}
