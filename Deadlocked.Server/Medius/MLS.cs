﻿using Deadlocked.Server.Accounts;
using Deadlocked.Server.Messages;
using Deadlocked.Server.Messages.Lobby;
using Deadlocked.Server.Messages.MGCL;
using Deadlocked.Server.Messages.RTIME;
using Medius.Crypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Deadlocked.Server.Medius
{
    public class MLS : BaseMediusComponent
    {
        public override int Port => 10078;
        private DateTime ticker = DateTime.UtcNow;

        static string[] INIT_1D_MESSAGES =
        {
            "05CDFA00000000080000010000BC9E77B0DC8DDCB08E41C4F637BDDEC9",
            "05CEFA000078100F00000100001FB479EDE006BE04AA78E639C51FD6F0",
            "05CFFA000000000C0000010000B5CEA072FCA3BF08C0A05EFA2106A1BE",
            "05D0FA0000000008000001000093E29AF5051FE14BA91ED2FF17E3C4C4",
            "05D1FA00000000080000010000BBCBDF273A184FE4BA735F867C25B7B2",
            "05D2FA000000000800000100000DA0152F16F5184C215A9B5C40299B5F",
            "05D3FA000000000C0000010000B17C90678DD09703C4AF77CDD5456424",
            "05D4FA000000000C00000100000DFD1196CE9E1219BFEAB4ED042CCEEE",
            "05D5FA000000000C0000010000898375D0A30EB86BC4A3A26E80CF675A",
            "05D6FA000000000C0000010000BFAC60651325AEC1A077262A6AB8312B",
            "05D7FA000000000C0000010000388DED87E97AF868F663C2B68F7028A5",
            "05D8FA000000000C0000010000EB12D96C966BB1C50C3D3881138B1306",
            "05D9FA000000000C000001000077183CF0128767D38DF9E6DE76B424CE",
            "05DAFA000000000C0000010000636220336C2301E4992F8D98C36A2A06",
            "05DBFA00000000080000010000A83C2C4AAFC257FF70C03C61F3C71C0F",
            "05DCFA00000001080000010000170FB1AF29A2A732AFEEA35A5E187E59",
            "05DDFA00008000080000010000D8360E93C93E708830F92BDF46D3AFC2"
        };

        public MLS()
        {
            _sessionCipher = new PS2_RC4(Utils.FromString(Program.KEY), CipherContext.RC_CLIENT_SESSION);
        }

        protected override void Tick(ClientSocket client)
        {
            List<BaseMessage> recv = new List<BaseMessage>();
            List<BaseMessage> responses = new List<BaseMessage>();

            lock (_queue)
            {
                while (_queue.Count > 0)
                    recv.Add(_queue.Dequeue());
            }

            // 
            foreach (var msg in recv)
                HandleCommand(msg, client, ref responses);


            if (false && (DateTime.UtcNow - ticker).TotalSeconds > 1)
            {
                ticker = DateTime.UtcNow;

                // Enable form party
                responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_MEMORY_POKE)
                {
                    Contents = Utils.FromString("24 57 36 01 04 00 00 00 04 00 00 00".Replace(" ", ""))
                });

                // Add game mode count
                responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_MEMORY_POKE)
                {
                    Contents = Utils.FromString("58 79 2A 01 01 00 00 00 06".Replace(" ", ""))
                });

                // Set infected
                responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_MEMORY_POKE)
                {
                    Contents = Utils.FromString("EC 7A 2A 01 09 00 00 00 49 6E 66 65 63 74 65 64 00".Replace(" ", ""))
                });
            }

            // 
            var targetMsgs = client.Client?.PullLobbyMessages();
            if (targetMsgs != null && targetMsgs.Count > 0)
                responses.AddRange(targetMsgs);

            // 
            if (shouldEcho)
                Echo(client, ref responses);

            responses.Send(client);
        }

        protected override int HandleCommand(BaseMessage message, ClientSocket client, ref List<BaseMessage> responses)
        {
            // 
            if (message.Id != RT_MSG_TYPE.RT_MSG_CLIENT_ECHO && message.Id != RT_MSG_TYPE.RT_MSG_SERVER_ECHO)
                Console.WriteLine($"MLS {client?.Client?.ClientAccount?.AccountName}: " + message.ToString());

            // 
            switch (message.Id)
            {
                case RT_MSG_TYPE.RT_MSG_CLIENT_HELLO:

                    new RT_MSG_SERVER_HELLO().Send(client);

                    break;
                case RT_MSG_TYPE.RT_MSG_CLIENT_CRYPTKEY_PUBLIC:
                    {
                        responses.Add(new RT_MSG_SERVER_CRYPTKEY_PEER() { Key = Utils.FromString(Program.KEY) });
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_TCP:
                    {
                        var m00 = message as RT_MSG_CLIENT_CONNECT_TCP;

                        client.SetToken(m00.AccessToken);

                        Console.WriteLine($"CLIENT CONNECTED TO MLS WITH SESSION KEY {m00.SessionKey} and ACCESS TOKEN {m00.AccessToken}");

                        if (client.Client == null)
                        {
                            responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_CLIENT_DISCONNECT_WITH_REASON) { Contents = new byte[1] });
                        }
                        else
                        {
                            client.Client.Status = MediusPlayerStatus.MediusPlayerInChatWorld;
                            responses.Add(new RT_MSG_SERVER_CONNECT_REQUIRE() { Contents = Utils.FromString("024802") });
                        }
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_READY_REQUIRE:
                    {
                        responses.Add(new RT_MSG_SERVER_CRYPTKEY_GAME() { Key = Utils.FromString(Program.KEY) });
                        responses.Add(new RT_MSG_SERVER_CONNECT_ACCEPT_TCP() { UNK_00 = 0x19, UNK_02 = 0xAF, UNK_03 = 0xAC, IP = (client.RemoteEndPoint as IPEndPoint).Address });
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_CLIENT_CONNECT_READY_TCP:
                    {
                        responses.Add(new RT_MSG_SERVER_CONNECT_COMPLETE() { ARG1 = 0x0001 });
                        responses.Add(new RT_MSG_SERVER_ECHO());
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_SERVER_ECHO:
                    {
                        client.Client?.OnEcho(DateTime.UtcNow);
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_SERVER_CHEAT_QUERY:
                    {
                        break;
                        int id1D = (message as RT_MSG_SERVER_CHEAT_QUERY).UNK_01;
                        int off1D = id1D - 0xCC;
                        switch (id1D)
                        {
                            case 0xCD:
                                {
                                    //responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_APP)
                                    //{
                                    //    Contents = Utils.FromString("0132303030303030303000000000000000000000000000000000000000004261646765723431000000000000000000000000000000000000000000000000B02B0000020000000100000000000000A3010000390300004E070000AF050000E3020000610400000000000000010000B454040008D800005AF4000008000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")
                                    //});
                                    //responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_APP)
                                    //{
                                    //    Contents = Utils.FromString("01D73100000000000000000000000000000000000000000000000000000063B2090053686F756C642049205472793F00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")
                                    //});

                                    if (off1D > client.ComponentState)
                                        client.ComponentState = off1D;

                                    // 05CEFA000078100F00000100001FB479EDE006BE04AA78E639C51FD6F0
                                    responses.Add(new RT_MSG_SERVER_CHEAT_QUERY()
                                    {
                                        UNK_00 = 0x05,
                                        UNK_01 = 0xCE,
                                        UNK_02 = 0xFA,
                                        UNK_05 = 0x1078,
                                        UNK_07 = 0x000F,
                                        UNK_09 = 0x0100,
                                        UNK_0D = Utils.FromString("1FB479EDE006BE04AA78E639C51FD6F0")
                                    });
                                    break;
                                }
                            case 0xCE:
                                {
                                    if (off1D > client.ComponentState)
                                        client.ComponentState = off1D;

                                    responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_CHEAT_QUERY)
                                    {
                                        Contents = Utils.FromString(INIT_1D_MESSAGES[client.ComponentState])
                                    });
                                    break;
                                }
                            case 0xCF:
                                {
                                    if (off1D > client.ComponentState)
                                        client.ComponentState = off1D;

                                    responses.Add(new RawMessage(RT_MSG_TYPE.RT_MSG_SERVER_CHEAT_QUERY)
                                    {
                                        Contents = Utils.FromString(INIT_1D_MESSAGES[client.ComponentState])
                                    });
                                    break;
                                }
                        }
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_CLIENT_APP_TOSERVER:
                    {
                        var appMsg = (message as RT_MSG_CLIENT_APP_TOSERVER).AppMessage;
                        if (appMsg == null || client == null || !client.Connected || client.Client == null)
                            break;

                        switch (appMsg.Id)
                        {
                            case MediusAppPacketIds.GetLadderStatsWide:
                                {
                                    var getLadderStatsWide = appMsg as MediusGetLadderStatsWideRequest;
                                    int[] stats = null;

                                    switch (getLadderStatsWide.LadderType)
                                    {
                                        case MediusLadderType.MediusLadderTypePlayer:
                                            {
                                                if (Program.Database.TryGetAccountById(getLadderStatsWide.AccountID_or_ClanID, out var account))
                                                    stats = account.AccountWideStats;
                                                break;
                                            }
                                        case MediusLadderType.MediusLadderTypeClan:
                                            {

                                                break;
                                            }
                                    }

                                    var response = new MediusGetLadderStatsWideResponse()
                                    {
                                        MessageID = getLadderStatsWide.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusSuccess,
                                        AccountID_or_ClanID = getLadderStatsWide.AccountID_or_ClanID
                                    };

                                    if (stats != null)
                                        response.Stats = stats;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = response
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.LadderList_ExtraInfo:
                                {
                                    var msg = appMsg as MediusLadderList_ExtraInfoRequest;
                                    var account = client.Client.ClientAccount;

                                    // ERROR -- Need to be logged in
                                    if (account == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusLadderList_ExtraInfoResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            AccountID = account.AccountId,
                                            AccountName = account.AccountName,
                                            AccountStats = account.Stats,
                                            LadderPosition = 0,
                                            LadderStat = account.AccountWideStats[msg.LadderStatIndex],
                                            OnlineState = new MediusPlayerOnlineState()
                                            {

                                            },
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            EndOfList = true
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.AccountUpdateStats:
                                {
                                    var msg = appMsg as MediusAccountUpdateStatsRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // Update stats
                                    if (client.Client.ClientAccount != null)
                                    {
                                        client.Client.ClientAccount.Stats = msg.Stats;
                                        Program.Database.Save();
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusAccountUpdateStatsResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            Response = 0
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.UpdateLadderStatsWide:
                                {
                                    var msg = appMsg as MediusUpdateLadderStatsWideRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    switch (msg.LadderType)
                                    {
                                        case MediusLadderType.MediusLadderTypePlayer:
                                            {
                                                client.Client.ClientAccount.AccountWideStats = msg.Stats;
                                                Program.Database.Save();

                                                responses.Add(new RT_MSG_SERVER_APP()
                                                {
                                                    AppMessage = new RawAppMessage(MediusAppPacketIds.UpdateLadderStatsWideResponse)
                                                    {
                                                        Contents = Utils.FromString("31 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00".Replace(" ", ""))
                                                    }
                                                });
                                                break;
                                            }
                                        case MediusLadderType.MediusLadderTypeClan:
                                            {
                                                responses.Add(new RT_MSG_SERVER_APP()
                                                {
                                                    AppMessage = new RawAppMessage(MediusAppPacketIds.UpdateLadderStatsWideResponse)
                                                    {
                                                        Contents = Utils.FromString("31 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00".Replace(" ", ""))
                                                    }
                                                });
                                                break;
                                            }
                                    }

                                    break;
                                }
                            case MediusAppPacketIds.GenericChatSetFilterRequest:
                                {
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGenericChatSetFilterResponse()
                                        {
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            ChatFilter = new MediusGenericChatFilter()
                                            {
                                                GenericChatFilterBitfield = Utils.FromString("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")
                                            }
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.SetAutoChatHistoryRequest:
                                {
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusSetAutoChatHistoryResponse()
                                        {
                                            StatusCode = MediusCallbackStatus.MediusSuccess
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.GetMyIP:
                                {
                                    var msg = appMsg as MediusGetMyIPRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetMyIPResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            IP = (client.RemoteEndPoint as IPEndPoint).Address,
                                            StatusCode = MediusCallbackStatus.MediusSuccess
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.GetAllAnnouncements:
                                {
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetAnnouncementsResponse()
                                        {
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            Announcement = Program.Settings.Announcement,
                                            AnnouncementID = 0,
                                            EndOfList = true
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.CheckMyClanInvitations:
                                {
                                    var msg = appMsg as MediusCheckMyClanInvitationsRequest;

                                    // Disabled for now
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusCheckMyClanInvitationsResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusNoResult
                                        }
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.CreateClan:
                                {
                                    var msg = appMsg as MediusCreateClanRequest;

                                    // Disable for now
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusCreateClanResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess
                                        }
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.GetMyClans:
                                {
                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetMyClansResponse()
                                        {
                                            StatusCode = MediusCallbackStatus.MediusClanNotFound,
                                            ClanID = -1,
                                            ApplicationID = Program.Settings.ApplicationId,
                                            ClanName = null,
                                            LeaderAccountID = client.Client.ClientAccount.AccountId,
                                            LeaderAccountName = client.Client.ClientAccount.AccountName,
                                            Stats = "000000000000000000000000",
                                            Status = MediusClanStatus.ClanDisbanded,
                                            EndOfList = true
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.FileCreate:
                                {
                                    var msg = appMsg as MediusFileCreateRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusFileCreateResponse()
                                        {
                                            StatusCode = MediusCallbackStatus.MediusDBError,
                                            MediusFileInfo = new MediusFile()
                                            {
                                                
                                            }
                                        }
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.FileDownload:
                                {
                                    var fileDownloadReq = appMsg as MediusFileDownloadRequest;

                                    // stats maybe?
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusFileDownloadResponse()
                                        {
                                            MessageID = fileDownloadReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusDBError
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.AccountGetID:
                                {
                                    var msg = appMsg as MediusAccountGetIDRequest;

                                    if (Program.Database.TryGetAccountByName(msg.AccountName, out var account))
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusAccountGetIDResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                AccountID = account.AccountId
                                            }
                                        });
                                    }
                                    else
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusAccountGetIDResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusAccountNotFound
                                            }
                                        });
                                    }

                                    break;
                                }
                            case MediusAppPacketIds.SetGameListFilter:
                                {
                                    var msg = appMsg as MediusSetGameListFilterRequest;

                                    // Set filter
                                    var filter = client.Client.SetGameListFilter(msg);

                                    // Give reply
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusSetGameListFilterResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = filter == null ? MediusCallbackStatus.MediusFail : MediusCallbackStatus.MediusSuccess,
                                            FilterID = filter?.FieldID ?? 0
                                        }
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.ClearGameListFilter:
                                {
                                    var msg = appMsg as MediusClearGameListFilterRequest;

                                    // Remove
                                    client.Client.ClearGameListFilter(msg.FilterID);

                                    // 
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusClearGameListFilterResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess
                                        }
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.AddToBuddyList:
                                {
                                    var msg = appMsg as MediusAddToBuddyListRequest;
                                    var statusCode = MediusCallbackStatus.MediusFail;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // find target player
                                    if (Program.Database.TryGetAccountById(msg.AccountID, out var targetAccount))
                                    {
                                        if (client.Client.ClientAccount.Friends.Contains(msg.AccountID))
                                        {

                                            statusCode = MediusCallbackStatus.MediusAccountAlreadyExists;
                                        }
                                        else
                                        {
                                            // targetAccount

                                            // Temporarily auto add player as friend
                                            // This should be replaced with a confirm/request system later
                                            client.Client.ClientAccount.Friends.Add(msg.AccountID);
                                            Program.Database.Save();

                                            statusCode = MediusCallbackStatus.MediusSuccess;
                                        }
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusAddToBuddyListResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = statusCode
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.RemoveFromBuddyList:
                                {
                                    var msg = appMsg as MediusRemoveFromBuddyListRequest;
                                    var statusCode = MediusCallbackStatus.MediusFail;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // 
                                    if (client.Client != null && client.Client.ClientAccount != null)
                                    {
                                        // find target friend
                                        if (client.Client.ClientAccount.Friends.Contains(msg.AccountID))
                                        {
                                            // remove
                                            client.Client.ClientAccount.Friends.Remove(msg.AccountID);
                                            Program.Database.Save();

                                            statusCode = MediusCallbackStatus.MediusSuccess;
                                        }
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusRemoveFromBuddyListResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = statusCode
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.GetBuddyList_ExtraInfo:
                                {
                                    var getBuddyListReq = appMsg as MediusGetBuddyList_ExtraInfoRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // Get friends
                                    var friends = client.Client.ClientAccount.Friends?.Select(x => Program.Database.GetAccountById(x)).Where(x => x != null);

                                    // Responses
                                    List<MediusGetBuddyList_ExtraInfoResponse> friendListResponses = new List<MediusGetBuddyList_ExtraInfoResponse>();

                                    // 
                                    if (friends != null)
                                    {
                                        // Iterate and send to client
                                        foreach (var friend in friends)
                                        {
                                            friendListResponses.Add(new MediusGetBuddyList_ExtraInfoResponse()
                                            {
                                                MessageID = getBuddyListReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                AccountID = friend.AccountId,
                                                AccountName = friend.AccountName,
                                                OnlineState = new MediusPlayerOnlineState()
                                                {
                                                    ConnectStatus = friend.IsLoggedIn ? (friend.Client?.Status ?? MediusPlayerStatus.MediusPlayerDisconnected) : MediusPlayerStatus.MediusPlayerDisconnected,
                                                    MediusLobbyWorldID = friend.Client?.CurrentChannel?.Id ?? Program.Settings.DefaultChannelId,
                                                    MediusGameWorldID = friend.Client?.CurrentGame?.Id ?? -1,
                                                },
                                                EndOfList = false
                                            });
                                        }
                                    }

                                    if (friendListResponses.Count > 0)
                                    {
                                        friendListResponses[friendListResponses.Count - 1].EndOfList = true;

                                        // Send friends
                                        responses.AddRange(friendListResponses.Select(x => new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = x
                                        }));
                                    }
                                    else
                                    {
                                        // No friends
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGetBuddyList_ExtraInfoResponse()
                                            {
                                                MessageID = getBuddyListReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                                EndOfList = true
                                            }
                                        });
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.GetClanMemberList_ExtraInfo:
                                {
                                    var getClanMemberListExtra = appMsg as MediusGetClanMemberList_ExtraInfoRequest;

                                    

                                    responses.AddRange(Program.Clients.Select(x => new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetClanMemberList_ExtraInfoResponse()
                                        {
                                            MessageID = getClanMemberListExtra.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusClanNotFound,
                                            /*
                                            AccountID = x.AccountId,
                                            AccountName = x.Username,
                                            OnlineState = new MediusPlayerOnlineState()
                                            {
                                                ConnectStatus = MediusPlayerStatus.MediusPlayerInChatWorld,
                                                GameName = "ABC",
                                                LobbyName = "123",
                                                MediusGameWorldID = 2,
                                                MediusLobbyWorldID = 2
                                            }
                                            */
                                        }
                                    }));

                                    /*
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetClanMemberList_ExtraInfoResponse()
                                        {
                                            MessageID = getClanMemberListExtra.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            AccountID = 2,
                                            AccountName = "URMOM",
                                            OnlineState = new MediusPlayerOnlineState()
                                            {
                                                ConnectStatus = MediusPlayerStatus.MediusPlayerInChatWorld,
                                                GameName = "ABC",
                                                LobbyName = "123",
                                                MediusGameWorldID = 2,
                                                MediusLobbyWorldID = 2
                                            },
                                            EndOfList = true
                                        }
                                    });
                                    */

                                    break;
                                }
                            case MediusAppPacketIds.GetClanInvitationsSent:
                                {
                                    var getClanInvSentReq = appMsg as MediusGetClanInvitationsSentRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetClanInvitationsSentResponse()
                                        {
                                            MessageID = getClanInvSentReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusNoResult,
                                            EndOfList = true
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.GetClanByID:
                                {
                                    var getClanByIdReq = appMsg as MediusGetClanByIDRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetClanByIDResponse()
                                        {
                                            MessageID = getClanByIdReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusNoResult,
                                            /*
                                            ApplicationID = Program.Settings.ApplicationId,
                                            ClanName = "DEV",
                                            LeaderAccountID = 1,
                                            LeaderAccountName = "Badger41",
                                            Status = MediusClanStatus.ClanActive
                                            */
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.FindPlayer:
                                {
                                    var findPlayerReq = appMsg as MediusFindPlayerRequest;
                                    Account account = null;

                                    if (findPlayerReq.SearchType == MediusPlayerSearchType.PlayerAccountID && !Program.Database.TryGetAccountById(findPlayerReq.ID, out account))
                                        account = null;
                                    else if (findPlayerReq.SearchType == MediusPlayerSearchType.PlayerAccountName && !Program.Database.TryGetAccountByName(findPlayerReq.Name, out account))
                                        account = null;

                                    if (account == null)
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusFindPlayerResponse()
                                            {
                                                MessageID = findPlayerReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusAccountNotFound,
                                                AccountID = findPlayerReq.ID,
                                                AccountName = findPlayerReq.Name,
                                                EndOfList = true
                                            }
                                        });
                                    }
                                    else
                                    {
                                        if (account.IsLoggedIn)
                                        {
                                            responses.Add(new RT_MSG_SERVER_APP()
                                            {
                                                AppMessage = new MediusFindPlayerResponse()
                                                {
                                                    MessageID = findPlayerReq.MessageID,
                                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                                    ApplicationID = Program.Settings.ApplicationId,
                                                    AccountID = account.AccountId,
                                                    AccountName = account.AccountName,
                                                    ApplicationType = (account.Client.Status == MediusPlayerStatus.MediusPlayerInGameWorld) ? MediusApplicationType.MediusAppTypeGame : MediusApplicationType.LobbyChatChannel,
                                                    ApplicationName = "?????",
                                                    MediusWorldID = (account.Client.Status == MediusPlayerStatus.MediusPlayerInGameWorld) ? account.Client.CurrentGameId : account.Client.CurrentChannelId,
                                                    EndOfList = true
                                                }
                                            });
                                        }
                                        else
                                        {
                                            responses.Add(new RT_MSG_SERVER_APP()
                                            {
                                                AppMessage = new MediusFindPlayerResponse()
                                                {
                                                    MessageID = findPlayerReq.MessageID,
                                                    StatusCode = MediusCallbackStatus.MediusNoResult,
                                                    EndOfList = true
                                                }
                                            });
                                        }
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.TextFilter:
                                {
                                    var textFilterReq = appMsg as MediusTextFilterRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusTextFilterResponse()
                                        {
                                            MessageID = textFilterReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            Text = textFilterReq.Text
                                        }
                                    });

                                    break;
                                }
                            case MediusAppPacketIds.BinaryMessage:
                                {
                                    var binaryMessage = appMsg as MediusBinaryMessage;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    switch (binaryMessage.MessageType)
                                    {
                                        case MediusBinaryMessageType.TargetBinaryMsg:
                                            {
                                                var target = Program.GetClientByAccountId(binaryMessage.TargetAccountID);

                                                target?.AddLobbyMessage(new RT_MSG_SERVER_APP()
                                                {
                                                    AppMessage = new MediusBinaryFwdMessage()
                                                    {
                                                        MessageType = binaryMessage.MessageType,
                                                        OriginatorAccountID = client.Client.ClientAccount.AccountId,
                                                        Message = binaryMessage.Message
                                                    }
                                                });
                                                break;
                                            }
                                        default:
                                            {
                                                Console.WriteLine($"Unhandled binary message type {binaryMessage.MessageType}");
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.GetTotalRankings:
                                {
                                    var msg = appMsg as MediusGetTotalRankingsRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }


                                    /*
                                    switch (msg.LadderType)
                                    {
                                        case MediusLadderType.MediusLadderTypeClan:
                                            {

                                                break;
                                            }
                                        case MediusLadderType.MediusLadderTypePlayer:
                                            {

                                                break;
                                            }
                                    }
                                    */

                                    //
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetTotalRankingsResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            TotalRankings = 0
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.LadderPosition_ExtraInfo:
                                {
                                    var ladderPosExtraReq = appMsg as MediusLadderPosition_ExtraInfoRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusLadderPosition_ExtraInfoResponse()
                                        {
                                            MessageID = ladderPosExtraReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.PlayerInfo:
                                {
                                    var playerInfoReq = appMsg as MediusPlayerInfoRequest;

                                    if (Program.Database.TryGetAccountById(playerInfoReq.AccountID, out var account))
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusPlayerInfoResponse()
                                            {
                                                MessageID = playerInfoReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                AccountName = account.AccountName,
                                                ApplicationID = Program.Settings.ApplicationId,
                                                PlayerStatus = account.IsLoggedIn ?  account.Client.Status : MediusPlayerStatus.MediusPlayerDisconnected,
                                                ConnectionClass = MediusConnectionType.Ethernet,
                                                Stats = account.Stats
                                            }
                                        });
                                    }
                                    else
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusPlayerInfoResponse()
                                            {
                                                MessageID = playerInfoReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusAccountNotFound
                                            }
                                        });
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.Policy:
                                {
                                    var policyReq = appMsg as MediusGetPolicyRequest;
                                    string policyText = policyReq.Policy == MediusPolicyType.Privacy ? Program.Settings.PrivacyPolicy : Program.Settings.UsagePolicy;

                                    responses.AddRange(MediusGetPolicyResponse.FromText(policyText).Select(x => new RT_MSG_SERVER_APP() { AppMessage = x }));
                                    break;
                                }
                            case MediusAppPacketIds.CreateChannel:
                                {
                                    var createChannelReq = appMsg as MediusCreateChannelRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // Create channel
                                    Channel channel = new Channel(createChannelReq);

                                    // Add
                                    Program.Channels.Add(channel);

                                    // Send to client
                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusCreateChannelResponse()
                                        {
                                            MessageID = createChannelReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            MediusWorldID = channel.Id
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.JoinChannel:
                                {
                                    var joinChannelReq = appMsg as MediusJoinChannelRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    var channel = Program.GetChannelById(joinChannelReq.MediusWorldID);
                                    if (channel == null)
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusJoinChannelResponse()
                                            {
                                                StatusCode = MediusCallbackStatus.MediusChannelNotFound
                                            }
                                        });
                                    }
                                    else if (channel.SecurityLevel == MediusWorldSecurityLevelType.WORLD_SECURITY_PLAYER_PASSWORD && joinChannelReq.LobbyChannelPassword != channel.Password)
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusJoinChannelResponse()
                                            {
                                                StatusCode = MediusCallbackStatus.MediusInvalidPassword
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // Tell previous channel player left
                                        var prevChannel = Program.Channels.FirstOrDefault(x => x.Id == client.Client.CurrentChannelId);
                                        prevChannel?.OnPlayerLeft(client.Client);

                                        // 
                                        client.Client.CurrentChannelId = channel.Id;

                                        // Tell channel
                                        channel.OnPlayerJoined(client.Client);

                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusJoinChannelResponse()
                                            {
                                                MessageID = joinChannelReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                ConnectInfo = new NetConnectionInfo()
                                                {
                                                    AccessKey = client.Client.Token,
                                                    SessionKey = client.Client.SessionKey,
                                                    WorldID = channel.Id,
                                                    ServerKey = new RSA_KEY(Program.GlobalAuthKey.N.ToByteArrayUnsigned().Reverse().ToArray()),
                                                    AddressList = new NetAddressList()
                                                    {
                                                        AddressList = new NetAddress[MediusConstants.NET_ADDRESS_LIST_COUNT]
                                                            {
                                                            new NetAddress() { Address = Program.SERVER_IP.ToString(), Port = (uint)Program.LobbyServer.Port, AddressType = NetAddressType.NetAddressTypeExternal},
                                                            new NetAddress() { AddressType = NetAddressType.NetAddressNone},
                                                            }
                                                    },
                                                    Type = NetConnectionType.NetConnectionTypeClientServerTCP
                                                }
                                            }
                                        });
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.ChannelInfo:
                                {
                                    var msg = appMsg as MediusChannelInfoRequest;

                                    // Find channel
                                    var channel = Program.GetChannelById(msg.MediusWorldID);

                                    if (channel == null)
                                    {
                                        // No channels
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusChannelInfoResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess
                                            }
                                        });
                                    }
                                    else
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusChannelInfoResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                LobbyName = channel.Name,
                                                ActivePlayerCount = channel.PlayerCount,
                                                MaxPlayers = channel.MaxPlayers
                                            }
                                        });
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.PlayerReport:
                                {
                                    var msg = appMsg as MediusPlayerReport;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // 
                                    if (client.Client.CurrentGameId == msg.MediusWorldID &&
                                        client.Client.SessionKey == msg.SessionKey)
                                    {
                                        client.Client.CurrentGame?.OnPlayerReport(msg);
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.WorldReport:
                                {
                                    var msg = appMsg as MediusWorldReport;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    client.Client.CurrentGame?.OnWorldReport(msg);

                                    break;
                                }
                            case MediusAppPacketIds.GetGameListFilter:
                                {
                                    var msg = appMsg as MediusGetGameListFilterRequest;

                                    var filters = client.Client.GameListFilters;

                                    if (filters == null || filters.Count == 0)
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGetGameListFilterResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                                EndOfList = true
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // Generate messages per filter
                                        var filterResponses = filters.Select(x => new MediusGetGameListFilterResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            BaselineValue = x.BaselineValue,
                                            ComparisonOperator = x.ComparisonOperator,
                                            FilterField = x.FilterField,
                                            FilterID = x.FieldID,
                                            Mask = x.Mask,
                                            EndOfList = false
                                        }).ToList();

                                        // Set end of list
                                        filterResponses[filterResponses.Count - 1].EndOfList = true;

                                        // Add to responses
                                        responses.AddRange(filterResponses.Select(x => new RT_MSG_SERVER_APP() { AppMessage = x }));
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.GameList_ExtraInfo:
                                {
                                    var msg = appMsg as MediusGameList_ExtraInfoRequest;

                                    var gameList = Program.Games
                                        .Where(x => x.WorldStatus == MediusWorldStatus.WorldActive || x.WorldStatus == MediusWorldStatus.WorldStaging)
                                        .Where(x => client.Client.IsGameMatch(x))
                                        .Select(x => new MediusGameList_ExtraInfoResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,

                                            GameHostType = x.GameHostType,
                                            GameLevel = x.GameLevel,
                                            GameName = x.GameName,
                                            GameStats = x.GameStats,
                                            GenericField1 = x.GenericField1,
                                            GenericField2 = x.GenericField2,
                                            GenericField3 = x.GenericField3,
                                            GenericField4 = x.GenericField4,
                                            GenericField5 = x.GenericField5,
                                            GenericField6 = x.GenericField6,
                                            GenericField7 = x.GenericField7,
                                            GenericField8 = x.GenericField8,
                                            MaxPlayers = (ushort)x.MaxPlayers,
                                            MediusWorldID = x.Id,
                                            MinPlayers = (ushort)x.MinPlayers,
                                            PlayerCount = (ushort)x.PlayerCount,
                                            PlayerSkillLevel = x.PlayerSkillLevel,
                                            RulesSet = x.RulesSet,
                                            SecurityLevel = (String.IsNullOrEmpty(x.GamePassword) ? MediusWorldSecurityLevelType.WORLD_SECURITY_NONE : MediusWorldSecurityLevelType.WORLD_SECURITY_PLAYER_PASSWORD),
                                            WorldStatus = x.WorldStatus,
                                            EndOfList = false
                                        }).ToArray();

                                    // Make last end of list
                                    if (gameList.Length > 0)
                                    {
                                        gameList[gameList.Length - 1].EndOfList = true;

                                        // Add to responses
                                        responses.AddRange(gameList.Select(x => new RT_MSG_SERVER_APP() { AppMessage = x }));
                                    }
                                    else
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGameList_ExtraInfoResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                                EndOfList = true
                                            }
                                        });
                                    }


                                    break;
                                }
                            case MediusAppPacketIds.GameInfo:
                                {
                                    var msg = appMsg as MediusGameInfoRequest;

                                    var game = Program.GetGameById(msg.MediusWorldID);

                                    if (game == null)
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGameInfoResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusGameNotFound
                                            }
                                        });
                                    }
                                    else
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGameInfoResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,

                                                GameHostType = game.GameHostType,
                                                GameLevel = game.GameLevel,
                                                GameName = game.GameName,
                                                GameStats = game.GameStats,
                                                GenericField1 = game.GenericField1,
                                                GenericField2 = game.GenericField2,
                                                GenericField3 = game.GenericField3,
                                                GenericField4 = game.GenericField4,
                                                GenericField5 = game.GenericField5,
                                                GenericField6 = game.GenericField6,
                                                GenericField7 = game.GenericField7,
                                                GenericField8 = game.GenericField8,
                                                MaxPlayers = (ushort)game.MaxPlayers,
                                                MinPlayers = (ushort)game.MinPlayers,
                                                PlayerCount = (ushort)game.PlayerCount,
                                                PlayerSkillLevel = game.PlayerSkillLevel,
                                                RulesSet = game.RulesSet,
                                                WorldStatus = game.WorldStatus,
                                                ApplicationID = Program.Settings.ApplicationId
                                            }
                                        });
                                    }

                                    break;
                                }
                            case MediusAppPacketIds.GameWorldPlayerList:
                                {
                                    var msg = appMsg as MediusGameWorldPlayerListRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    var game = Program.GetGameById(msg.MediusWorldID);
                                    if (game == null)
                                    {
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGameWorldPlayerListResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusGameNotFound
                                            }
                                        });
                                    }
                                    else
                                    {
                                        var playerList = game.Clients.Where(x => x != null && x.Client.IsConnected).Select(x => new MediusGameWorldPlayerListResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            AccountID = x.Client.ClientAccount.AccountId,
                                            AccountName = x.Client.ClientAccount.AccountName,
                                            ConnectionClass = MediusConnectionType.Ethernet,
                                            EndOfList = false
                                        }).ToArray();

                                        // Set last end of list
                                        if (playerList.Length > 0)
                                            playerList[playerList.Length - 1].EndOfList = true;

                                        responses.AddRange(playerList.Select(x => new RT_MSG_SERVER_APP() { AppMessage = x }));
                                    }

                                    break;
                                }
                            case MediusAppPacketIds.SetLobbyWorldFilter:
                                {
                                    var msg = appMsg as MediusSetLobbyWorldFilterRequest;


                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusSetLobbyWorldFilterResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            FilterMask1 = msg.FilterMask1,
                                            FilterMask2 = msg.FilterMask2,
                                            FilterMask3 = msg.FilterMask3,
                                            FilterMask4 = msg.FilterMask4,
                                            FilterMaskLevel = msg.FilterMaskLevel,
                                            LobbyFilterType = msg.LobbyFilterType
                                        }
                                    });


                                    break;
                                }
                            case MediusAppPacketIds.ChannelList:
                                {
                                    var msg = appMsg as MediusChannelListRequest;
                                    var gameChannels = Program.Channels.Where(x => x.Type == ChannelType.Game);
                                    List<MediusChannelListResponse> channelResponses = new List<MediusChannelListResponse>();

                                    foreach (var channel in gameChannels)
                                    {
                                        if (channel.Type != ChannelType.Game)
                                            continue;

                                        channelResponses.Add(new MediusChannelListResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            MediusWorldID = channel.Id,
                                            LobbyName = channel.Name,
                                            PlayerCount = channel.PlayerCount,
                                            EndOfList = false
                                        });
                                    }

                                    if (channelResponses.Count == 0)
                                    {
                                        // Return none
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusChannelListResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                                EndOfList = true
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // Ensure the end of list flag is set
                                        channelResponses[channelResponses.Count - 1].EndOfList = true;

                                        // Add to responses
                                        responses.AddRange(channelResponses.Select(x => new RT_MSG_SERVER_APP() { AppMessage = x }));
                                    }


                                    break;
                                }
                            case MediusAppPacketIds.ChannelList_ExtraInfo:
                                {
                                    var msg = appMsg as MediusChannelList_ExtraInfoRequest;

                                    // Deadlocked only uses this to connect to a non-game channel (lobby)
                                    // So we'll filter by lobby here
                                    var channels = Program.Channels.Where(x => x.Type == ChannelType.Lobby);
                                    List<MediusChannelList_ExtraInfoResponse> channelResponses = new List<MediusChannelList_ExtraInfoResponse>();

                                    foreach (var channel in channels)
                                    {
                                        channelResponses.Add(new MediusChannelList_ExtraInfoResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            MediusWorldID = channel.Id,
                                            LobbyName = channel.Name,
                                            GameWorldCount = (ushort)channel.GameCount,
                                            PlayerCount = (ushort)channel.PlayerCount,
                                            MaxPlayers = (ushort)channel.MaxPlayers,
                                            GenericField1 = channel.GenericField1,
                                            GenericField2 = channel.GenericField2,
                                            GenericField3 = channel.GenericField3,
                                            GenericField4 = channel.GenericField4,
                                            GenericFieldLevel = channel.GenericFieldLevel,
                                            SecurityLevel = channel.SecurityLevel,
                                            EndOfList = false
                                        });
                                    }

                                    if (channelResponses.Count == 0)
                                    {
                                        // Return none
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusChannelList_ExtraInfoResponse()
                                            {
                                                MessageID = msg.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                                EndOfList = true
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // Ensure the end of list flag is set
                                        channelResponses[channelResponses.Count - 1].EndOfList = true;

                                        // Add to responses
                                        responses.AddRange(channelResponses.Select(x => new RT_MSG_SERVER_APP() { AppMessage = x }));
                                    }


                                    break;
                                }
                            case MediusAppPacketIds.EndGameReport:
                                {
                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    if (appMsg is MediusEndGameReport msg)
                                    {
                                        client.Client.CurrentGame?.OnEndGameReport(msg);
                                    }

                                    break;
                                }
                            case MediusAppPacketIds.GetIgnoreList:
                                {
                                    var ignoreListReq = appMsg as MediusGetIgnoreListRequest;

                                    // Get ignored players
                                    var ignored = client.Client.ClientAccount.Ignored.Select(x => Program.Database.GetAccountById(x)).Where(x => x != null);

                                    // Responses
                                    List<MediusGetIgnoreListResponse> ignoredListResponses = new List<MediusGetIgnoreListResponse>();

                                    // Iterate and send to client
                                    foreach (var player in ignored)
                                    {
                                        ignoredListResponses.Add(new MediusGetIgnoreListResponse()
                                        {
                                            MessageID = ignoreListReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            IgnoreAccountID = player.AccountId,
                                            IgnoreAccountName = player.AccountName,
                                            PlayerStatus = player.Client?.Status ?? MediusPlayerStatus.MediusPlayerDisconnected,
                                            EndOfList = false
                                        });
                                    }

                                    if (ignoredListResponses.Count > 0)
                                    {
                                        // End list
                                        ignoredListResponses[ignoredListResponses.Count - 1].EndOfList = true;

                                        // Send ignored list
                                        responses.AddRange(ignoredListResponses.Select(x => new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = x
                                        }));
                                    }
                                    else
                                    {
                                        // No ignored
                                        responses.Add(new RT_MSG_SERVER_APP()
                                        {
                                            AppMessage = new MediusGetIgnoreListResponse()
                                            {
                                                MessageID = ignoreListReq.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                                EndOfList = true
                                            }
                                        });
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.AddToIgnoreList:
                                {
                                    var msg = appMsg as MediusAddToIgnoreListRequest;
                                    var statusCode = MediusCallbackStatus.MediusFail;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // find target player
                                    if (Program.Database.TryGetAccountById(msg.IgnoreAccountID, out var targetAccount))
                                    {
                                        // Ensure they're not already ignored
                                        if (client.Client.ClientAccount.Ignored.Contains(msg.IgnoreAccountID))
                                        {
                                            statusCode = MediusCallbackStatus.MediusAccountAlreadyExists;
                                        }
                                        else
                                        {
                                            // Remove from friends if a friend
                                            client.Client.ClientAccount.Friends.Remove(msg.IgnoreAccountID);
                                            client.Client.ClientAccount.Ignored.Add(msg.IgnoreAccountID);
                                            Program.Database.Save();

                                            statusCode = MediusCallbackStatus.MediusSuccess;
                                        }
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusAddToIgnoreListResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = statusCode
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.RemoveFromIgnoreList:
                                {
                                    var msg = appMsg as MediusRemoveFromIgnoreListRequest;
                                    var statusCode = MediusCallbackStatus.MediusFail;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // 
                                    if (client.Client != null && client.Client.ClientAccount != null)
                                    {
                                        // find target ignored
                                        if (client.Client.ClientAccount.Ignored.Contains(msg.IgnoreAccountID))
                                        {
                                            // remove
                                            client.Client.ClientAccount.Ignored.Remove(msg.IgnoreAccountID);
                                            Program.Database.Save();

                                            statusCode = MediusCallbackStatus.MediusSuccess;
                                        }
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusRemoveFromIgnoreListResponse()
                                        {
                                            MessageID = msg.MessageID,
                                            StatusCode = statusCode
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.AccountLogout:
                                {
                                    var accountLogoutReq = appMsg as MediusAccountLogoutRequest;
                                    bool success = false;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // Check token
                                    if (accountLogoutReq.SessionKey == client.Client.SessionKey)
                                    {
                                        success = true;

                                        // 
                                        Console.WriteLine($"{client.Client.ClientAccount.AccountName} has logged out.");

                                        // Logout
                                        client.Client.Logout();
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusAccountLogoutResponse()
                                        {
                                            StatusCode = success ? MediusCallbackStatus.MediusSuccess : MediusCallbackStatus.MediusAccountNotFound
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.UpdateUserState:
                                {
                                    var updateUserReq = appMsg as MediusUpdateUserState;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    client.Client.Action = updateUserReq.UserAction;

                                    switch (updateUserReq.UserAction)
                                    {
                                        case MediusUserAction.JoinedChatWorld:
                                        case MediusUserAction.LeftGameWorld:
                                            {
                                                client.Client.Status = MediusPlayerStatus.MediusPlayerInChatWorld;
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.GetServerTimeRequest:
                                {
                                    var getServerTimeReq = appMsg as MediusGetServerTimeRequest;

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusGetServerTimeResponse()
                                        {
                                            MessageID = getServerTimeReq.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            Local_server_timezone = MediusTimeZone.MediusTimeZone_GMT 
                                        }
                                    });
                                    break;
                                }
                            case MediusAppPacketIds.CreateGame:
                                {
                                    var createGameReq = appMsg as MediusCreateGameRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    Program.ProxyServer.CreateGame(client, createGameReq);
                                    break;
                                }
                            case MediusAppPacketIds.JoinGame:
                                {
                                    var joinGameReq = appMsg as MediusJoinGameRequest;

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    Program.ProxyServer.JoinGame(client, joinGameReq);
                                    break;
                                }
                            case MediusAppPacketIds.GenericChatMessage:
                                {
                                    var genericChatMessage = appMsg as MediusGenericChatMessage;
                                    var channel = client.Client.CurrentChannel;
                                    List<BaseMessage> chatResponses = new List<BaseMessage>();

                                    // ERROR -- Need to be logged in
                                    if (client.Client.ClientAccount == null)
                                    {
                                        client.Disconnect();
                                        break;
                                    }

                                    // Need to be in a channel
                                    if (channel == null)
                                        break;

                                    switch (genericChatMessage.MessageType)
                                    {
                                        case MediusChatMessageType.Broadcast:
                                            {
                                                IEnumerable<ClientObject> players = null;

                                                switch (genericChatMessage.Message.Substring(1))
                                                {
                                                    case "/roll":
                                                        {
                                                            players = channel.Clients.Select(x => x.Client);
                                                            chatResponses.Add(new RT_MSG_SERVER_APP()
                                                            {
                                                                AppMessage = new MediusGenericChatFwdMessage()
                                                                {
                                                                    OriginatorAccountID = 0,
                                                                    OriginatorAccountName = "SYSTEM",
                                                                    Message = $"APlayer {client.Client?.ClientAccount?.AccountName ?? "ERROR"} rolled " + RNG.Next(0, 100).ToString(),
                                                                    MessageType = genericChatMessage.MessageType,
                                                                    TimeStamp = Utils.GetUnixTime()
                                                                }
                                                            });
                                                            break;
                                                        }
                                                    default:
                                                        {
                                                            players = channel.Clients.Where(x => x.Client != client.Client).Select(x => x.Client);
                                                            chatResponses.Add(new RT_MSG_SERVER_APP()
                                                            {
                                                                AppMessage = new MediusGenericChatFwdMessage()
                                                                {
                                                                    OriginatorAccountID = client.Client.ClientAccount.AccountId,
                                                                    OriginatorAccountName = client.Client.ClientAccount.AccountName,
                                                                    Message = genericChatMessage.Message,
                                                                    MessageType = genericChatMessage.MessageType,
                                                                    TimeStamp = Utils.GetUnixTime()
                                                                }
                                                            });
                                                            break;
                                                        }
                                                }

                                                // 
                                                if (chatResponses != null && chatResponses.Count > 0 && players != null)
                                                    foreach (var player in players)
                                                        player.AddLobbyMessages(chatResponses);
                                                break;
                                            }
                                        default:
                                            {
                                                Console.WriteLine($"Unhandled generic chat message type:{genericChatMessage.MessageType} {genericChatMessage}");
                                                break;
                                            }
                                    }
                                    break;
                                }
                            case MediusAppPacketIds.SessionEnd:
                                {
                                    var sessionEndReq = appMsg as MediusSessionEndRequest;
                                    bool success = false;


                                    // Check token
                                    if (sessionEndReq.SessionKey == client.Client.SessionKey)
                                    {
                                        // 
                                        success = true;
                                    }

                                    responses.Add(new RT_MSG_SERVER_APP()
                                    {
                                        AppMessage = new MediusSessionEndResponse()
                                        {
                                            MessageID = sessionEndReq.MessageID,
                                            StatusCode = success ? MediusCallbackStatus.MediusSuccess : MediusCallbackStatus.MediusEndSessionFailed
                                        }
                                    });
                                    break;
                                }
                            default:
                                {
                                    Console.WriteLine($"MLS Unhandled App Message: {appMsg.Id} {appMsg}");
                                    break;
                                }
                        }
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_CLIENT_ECHO:
                    {
                        responses.Add(new RT_MSG_CLIENT_ECHO() { Value = (message as RT_MSG_CLIENT_ECHO).Value });
                        break;
                    }
                case RT_MSG_TYPE.RT_MSG_CLIENT_DISCONNECT:
                case RT_MSG_TYPE.RT_MSG_CLIENT_DISCONNECT_WITH_REASON:
                    {
                        client.Disconnect();
                        break;
                    }
                default:
                    {
                        Console.WriteLine($"MLS Unhandled Medius Command: {message.Id} {message}");
                        break;
                    }
            }

            return 0;
        }
    }
}