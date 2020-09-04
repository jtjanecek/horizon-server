﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Plugins
{
    public enum PluginEvent
    {
        MEDIUS_PLAYER_ON_LOGGED_IN,
        MEDIUS_PLAYER_ON_LOGGED_OUT,
        MEDIUS_PLAYER_ON_CREATE_GAME,
        MEDIUS_PLAYER_ON_JOIN_GAME,
        MEDIUS_PLAYER_ON_JOINED_GAME,
        MEDIUS_PLAYER_ON_LEFT_GAME,
        MEDIUS_PLAYER_ON_CHAT_MESSAGE,

        MEDIUS_GAME_ON_CREATED,
        MEDIUS_GAME_ON_DESTROYED,
        MEDIUS_GAME_ON_STARTED,
        MEDIUS_GAME_ON_ENDED,
        MEDIUS_GAME_ON_HOST_LEFT
    }
}
