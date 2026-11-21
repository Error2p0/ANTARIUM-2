using System;
using Antarium.Content;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Why the demo dialog opens.</summary>
    public enum DemoDialogKind : byte
    {
        /// <summary>The colony reached the Ancient Seal.</summary>
        Seal,
        /// <summary>Played the demo for a while (offered once).</summary>
        Time,
        /// <summary>The Flight or Genes button: the Nuptial Flight is in the full game.</summary>
        Flight,
        /// <summary>From the Esc menu or the title screen: what the full game adds.</summary>
        FullGame,
    }

    /// <summary>
    /// The demo (GDD 2.16): the end-of-demo screen (the seal, or after a while of play) and the full game teaser, with what the
    /// full game adds, a note that the colony carries over, Wishlist on Steam and Keep playing. Texts come from <see cref="DemoConfig"/>.
    /// </summary>
    public sealed class DemoDialog
    {
        readonly DemoConfig _def;
        readonly Label _title, _body, _carry;
        readonly Button _close;

        public VisualElement Root { get; }
        public event Action Wishlist, Closed;
        public DemoDialogKind Kind { get; private set; }

        public DemoDialog(DemoConfig def)
        {
            _def = def;
            Root = new VisualElement();
            Root.AddToClassList("panel");
            Root.AddToClassList("dialog");
            Root.AddToClassList("demo");
            _title = Dialogs.Text(Root, "", "dialog__title");
            _body = Dialogs.Text(Root, "", "dialog__text");
            Dialogs.Text(Root, "The full game adds", "demo__heading");
            foreach (var line in def.fullGameFeatures)
                if (!string.IsNullOrWhiteSpace(line)) Dialogs.Text(Root, "- " + line, "demo__feature");
            _carry = Dialogs.Text(Root, def.carryOver, "dialog__note");
            var wishlist = Dialogs.Button(Root, "Wishlist on Steam", () => Wishlist?.Invoke());
            wishlist.AddToClassList("menu-button--wishlist");
            _close = Dialogs.Button(Root, "Keep playing", () => Closed?.Invoke());
        }

        public void Show(DemoDialogKind kind)
        {
            Kind = kind;
            _title.text = kind switch
            {
                DemoDialogKind.Flight => _def.flightTitle + " - full game",
                DemoDialogKind.FullGame => "Antarium 2 - the full game",
                _ => _def.endTitle,
            };
            _body.text = kind switch
            {
                DemoDialogKind.Seal => _def.endBodySeal,
                DemoDialogKind.Time => _def.endBodyTime,
                DemoDialogKind.Flight => _def.flightBody,
                _ => "This is the demo: the first colony, down to the Ancient Seal at the bottom of the stone strata.",
            };
            _close.text = kind is DemoDialogKind.Seal or DemoDialogKind.Time ? "Keep playing" : "Close";
            _carry.EnableInClassList("hidden", string.IsNullOrWhiteSpace(_def.carryOver));
        }
    }
}
