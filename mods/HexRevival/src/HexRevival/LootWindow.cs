using System;

namespace HexRevival
{
    /// <summary>
    /// The loot window ("LootDisplayWindow", one for the whole front end) and its OnLootWindowClosed callbacks.
    /// Every screen adds its callback for the window it shows, but not all of them take it off again:
    /// UIPackListViewModel removes its own methods, UISystem.DisplayLootWindow wraps the caller's in a lambda nobody
    /// can remove, the arena and the login gifts never remove theirs. What is left fires again at the next loot
    /// window of any screen. On the packs screen (a bug the game always had): after a chest was opened,
    /// DisplayLootWindow's lambda stays, so the next spin's reward window also calls OnLootWindowClosed → ShowUI
    /// (pack list back, slot buttons gone) while OnLootClosed keeps the chest over the well — the stuck mix of both
    /// states. Here, every callback the window had when it closed is taken off after it ran: each fires once, for the
    /// window it was added for (callbacks added by those callbacks, for a next window, are kept).
    /// </summary>
    public static class LootWindow
    {
        /// <summary>UIGenericLootWindow.OnCloseWindow, before: the callbacks about to run.</summary>
        public static void Closing(UIGenericLootWindow w, ref UIGenericLootWindow._OnLootWindowClosed state)
        {
            state = w.OnLootWindowClosed;
        }

        /// <summary>UIGenericLootWindow.OnCloseWindow, after: those callbacks are taken off (one by one, as some took themselves off already).</summary>
        public static void Closed(UIGenericLootWindow w, UIGenericLootWindow._OnLootWindowClosed state)
        {
            if (state == null || w.OnLootWindowClosed == null) return;
            Delegate d = w.OnLootWindowClosed;
            foreach (Delegate one in state.GetInvocationList()) d = Delegate.Remove(d, one);
            w.OnLootWindowClosed = (UIGenericLootWindow._OnLootWindowClosed)d;
        }
    }
}
