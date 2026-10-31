namespace HexRevival
{
    /// <summary>
    /// The game's connection to the official servers (HConnect, closed since 2019) is no longer attempted (Plugin:
    /// Connector.tryConnect). It retried every 30 seconds, each time with "Failed to connect.", "Sleeping for connection
    /// retry" and "SES_STATE CONNECTING -> CONNECTING", and the client asks it for the player's name ("Asked for player
    /// name, but no info yet."): about two thirds of the log after an hour. Those messages are left out (one failure kept,
    /// should a connection be tried anyway). Nothing else is filtered.
    /// </summary>
    public static class LogFilter
    {
        private static bool s_FailureSeen;

        /// <summary>LogBase.Debug: true to leave the message out.</summary>
        public static bool DropDebug(string format, object[] args)
        {
            if (format == "Sleeping for connection retry" || format == "Asked for player name, but no info yet.") return true;
            // A state "changing" to itself (the retries); real changes stay.
            return format == "SES_STATE {0} -> {1}" && args != null && args.Length == 2 && Equals(args[0], args[1]);
        }

        /// <summary>LogBase.Error: true to leave the message out.</summary>
        public static bool DropError(string logger, string format)
        {
            if (format != "Failed to connect." || logger == null || logger.IndexOf("Hconnect", System.StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (s_FailureSeen) return true;
            s_FailureSeen = true;
            return false;
        }
    }
}
