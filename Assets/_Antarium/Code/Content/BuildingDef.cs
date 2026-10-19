using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum BuildingKind : byte
    {
        /// <summary>Horizontal transport cell (left / right).</summary>
        Belt,
        /// <summary>Vertical transport cell (up / down).</summary>
        Lift,
        /// <summary>Hands items round-robin to the transport cells and machines around it.</summary>
        Splitter,
        /// <summary>Like a splitter, but items of the filter type only leave in its arrow direction.</summary>
        Filter,
        /// <summary>Multi-cell machine that runs recipes.</summary>
        Processor,
    }

    /// <summary>Which generated placeholder sprite a machine uses until real art exists.</summary>
    public enum PlaceholderArt : byte
    {
        Block,
        Kiln,
        Garden,
    }

    /// <summary>Anything the player can place with the Build tool: transport cells and processing chambers.</summary>
    [CreateAssetMenu(menuName = "Antarium/Building", fileName = "Building")]
    public sealed class BuildingDef : ContentDef
    {
        public BuildingKind kind;
        [Tooltip("Position in the build palette (lower first).")]
        public int paletteOrder;
        [TextArea] public string description;

        [Header("Placement & construction")]
        [Tooltip("Footprint in cells. Transport cells are always 1x1.")]
        public Vector2Int size = Vector2Int.one;
        [Tooltip("Paid from the colony stock when placed; refunded when removed.")]
        public List<ItemAmount> cost = new();
        [Min(0f)] public float foodCost;
        [Tooltip("Seconds of work for one worker to build it.")]
        [Min(0.05f)] public float buildWork = 1f;

        [Header("Transport")]
        [Tooltip("Cells per second items move along this cell.")]
        [Min(0.1f)] public float speed = 2f;

        [Header("Processing")]
        public List<RecipeDef> recipes = new();
        [Tooltip("Input buffer per item type, in crafts' worth of that input.")]
        [Min(1)] public int inputBatches = 2;
        [Tooltip("Production pauses while this many products wait at the machine (no output belt).")]
        [Min(1)] public int outputLimit = 6;

        [Header("Presentation")]
        [Tooltip("Colour of the palette swatch.")]
        public Color color = Color.white;
        [Tooltip("Generated sprite for machines (transport cells have their own).")]
        public PlaceholderArt art;

        public bool IsTransport => kind != BuildingKind.Processor;
        public Vector2Int Footprint => IsTransport ? Vector2Int.one : new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
    }
}
