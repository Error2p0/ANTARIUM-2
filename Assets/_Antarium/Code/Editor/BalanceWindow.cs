using System.Collections.Generic;
using System.Linq;
using System.Text;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// Antarium/Balance: cost tables read straight from the content (research, contracts, castes, genetic traits,
    /// prestige) and headless colony runs with the <see cref="Autopilot"/> (rows per game minute, copyable as CSV).
    /// </summary>
    public sealed class BalanceWindow : EditorWindow
    {
        static readonly string[] Tabs = { "Headless run", "Research", "Contracts", "Castes", "Genetics", "Prestige" };

        int _tab;
        Vector2 _scroll;
        float _minutes = 30f;
        uint _seed = 12345;
        List<BalanceSample> _samples;
        string _runInfo;

        [MenuItem("Antarium/Balance")]
        static void Open() => GetWindow<BalanceWindow>("Antarium Balance");

        static ContentDatabase Database => AssetDatabase.LoadAssetAtPath<ContentDatabase>(ContentSetup.DatabasePath);
        static GameConfig Config => AssetDatabase.LoadAssetAtPath<GameConfig>(ContentSetup.ConfigPath);

        void OnGUI()
        {
            _tab = GUILayout.Toolbar(_tab, Tabs);
            var db = Database;
            var config = Config;
            if (db == null || config == null)
            {
                EditorGUILayout.HelpBox("Run Antarium/Setup/Create Default Content first.", MessageType.Warning);
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: RunTab(db, config); break;
                case 1: ResearchTab(db); break;
                case 2: ContractsTab(db); break;
                case 3: CastesTab(db); break;
                case 4: GeneticsTab(db); break;
                default: PrestigeTab(config); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- headless run

        void RunTab(ContentDatabase db, GameConfig config)
        {
            EditorGUILayout.HelpBox("Plays a fresh colony without rendering: a 3-wide shaft with side branches, storage in the branches, " +
                                    "contracts claimed, the cheapest research bought. No buildings. Shows how fast the hand-run early game moves.",
                MessageType.Info);
            _minutes = EditorGUILayout.Slider("Game minutes", _minutes, 1f, 120f);
            _seed = (uint)math.max(1, EditorGUILayout.IntField("Seed", (int)_seed));
            if (GUILayout.Button("Run")) Run(db, config);
            if (_samples == null) return;
            EditorGUILayout.LabelField(_runInfo);
            if (GUILayout.Button("Copy CSV")) EditorGUIUtility.systemCopyBuffer = BalanceRun.ToCsv(_samples);
            Row("min", "ants", "food", "knowl.", "depth", "research", "contracts", "dug", "stored", "memory*");
            foreach (var s in _samples)
                Row(s.Minutes.ToString("0"), s.Ants.ToString(), s.Food.ToString("0"), s.Knowledge.ToString("0"), s.Depth.ToString(),
                    s.Research.ToString(), s.Contracts.ToString(), s.CellsDug.ToString(), s.Stored.ToString(), s.Memory.ToString());
            EditorGUILayout.LabelField("* Genetic Memory a flight with one princess would earn at that point.", EditorStyles.miniLabel);
        }

        void Run(ContentDatabase db, GameConfig config)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var content = ContentDB.Build(db);
            try
            {
                using var sim = new Simulation(content, config, _seed);
                _samples = BalanceRun.Run(sim, _minutes, 60f, p => EditorUtility.DisplayCancelableProgressBar("Balance run", $"{p * _minutes:0} of {_minutes:0} game minutes", p));
                _runInfo = $"Seed {_seed}: {_samples[^1].Minutes:0.0} game minutes in {started.Elapsed.TotalSeconds:0.0} s real time.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                content.Dispose();
            }
        }

        // ---------------------------------------------------------------- tables

        static void ResearchTab(ContentDatabase db)
        {
            Row("tier", "research", "knowledge", "items / food", "sum knowledge");
            float sum = 0f;
            foreach (var r in db.research.Where(r => r != null).OrderBy(r => r.treePosition.x).ThenBy(r => r.treePosition.y))
            {
                sum += r.knowledgeCost;
                string items = string.Join(", ", r.cost.Where(c => c.item != null).Select(c => $"{c.count} {c.item.DisplayName}"));
                if (r.foodCost > 0f) items += (items.Length > 0 ? ", " : "") + $"{r.foodCost:0} food";
                Row((r.treePosition.x + 1).ToString(), r.DisplayName, r.knowledgeCost.ToString("0"), items, sum.ToString("0"));
            }
        }

        static void ContractsTab(ContentDatabase db)
        {
            EditorGUILayout.LabelField("Repeatable contracts: amount and knowledge reward for the 1st, 2nd, 3rd, 5th and 10th time.", EditorStyles.miniLabel);
            Row("contract", "1st", "2nd", "3rd", "5th", "10th");
            foreach (var c in db.contracts.Where(c => c != null).OrderBy(c => c.introOrder).ThenBy(c => c.Id))
            {
                if (!c.repeatable)
                {
                    Row(c.DisplayName + " (intro)", $"{c.amount} / {c.rewardKnowledge:0}k", "", "", "", "");
                    continue;
                }
                string At(int n)
                {
                    float scale = math.pow(1f + c.growth, n - 1);
                    long amount = (long)math.round(c.amount * scale);
                    return $"{amount} / {c.rewardKnowledge * amount / c.amount:0}k";
                }
                Row(c.DisplayName, At(1), At(2), At(3), At(5), At(10));
            }
        }

        static void CastesTab(ContentDatabase db)
        {
            Row("caste", "egg food", "upkeep/min", "food/min per 100", "HP", "speed", "dig", "carry");
            foreach (var c in db.castes.Where(c => c != null).OrderBy(c => c.order))
                Row(c.DisplayName, c.eggFoodCost.ToString("0.#"), c.upkeep.ToString("0.00"), (c.upkeep * 100f).ToString("0"),
                    c.health.ToString("0"), c.speed.ToString("0.##"), c.dig.ToString("0.##"), c.carry.ToString());
        }

        static void GeneticsTab(ContentDatabase db)
        {
            Row("tier", "trait", "levels", "cost per level", "total");
            foreach (var g in db.genetics.Where(g => g != null).OrderBy(g => g.treePosition.x).ThenBy(g => g.treePosition.y))
            {
                int levels = math.max(1, g.maxLevel);
                var costs = Enumerable.Range(0, levels).Select(l => math.max(0, g.cost) + math.max(0, g.costPerLevel) * l).ToArray();
                Row((g.treePosition.x + 1).ToString(), g.DisplayName, levels.ToString(), string.Join(" ", costs), costs.Sum().ToString());
            }
        }

        static void PrestigeTab(GameConfig config)
        {
            var p = config.prestige;
            EditorGUILayout.LabelField($"Score = depth x {p.depthScore} + knowledge earned x {p.knowledgeScore} + research x {p.researchScore} + contracts x {p.contractScore}.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField($"Memory = floor((score / {p.scoreDivisor}) ^ {p.scoreExponent} x princess factor x gain); " +
                                       $"princess factor = 1 + {p.princessBonus} per princess after the first (up to {p.maxPrincesses}).",
                EditorStyles.wordWrappedMiniLabel);
            Row("score", "1 princess", "3 princesses", $"{p.maxPrincesses} princesses");
            foreach (float score in new[] { 25f, 50f, 100f, 200f, 400f, 800f, 1600f, 3200f, 6400f })
            {
                float baseMemory = math.pow(score / math.max(0.01f, p.scoreDivisor), math.clamp(p.scoreExponent, 0.1f, 1f));
                string With(int princesses) => math.floor(baseMemory * (1f + p.princessBonus * (math.min(princesses, p.maxPrincesses) - 1))).ToString("0");
                Row(score.ToString("0"), With(1), With(3), With(p.maxPrincesses));
            }
        }

        static void Row(params string[] cells)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < cells.Length; i++)
                GUILayout.Label(cells[i], i == 0 ? GUILayout.Width(170) : GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
        }
    }
}
