using Deadlocked.Server.Stream;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Deadlocked.Server.Medius.Models.Packets.Lobby
{
	[MediusMessage(NetMessageTypes.MessageClassLobby, MediusLobbyMessageIds.AccountRegistration)]
    public class MediusAccountRegistrationRequest : BaseLobbyMessage
    {
		public override byte PacketType => (byte)MediusLobbyMessageIds.AccountRegistration;

        public string SessionKey; // SESSIONKEY_MAXLEN
        public MediusAccountType AccountType;
        public string AccountName; // ACCOUNTNAME_MAXLEN
        public string Password; // PASSWORD_MAXLEN

        public override void Deserialize(BinaryReader reader)
        {
            // 
            base.Deserialize(reader);

            // 
            SessionKey = reader.ReadString(MediusConstants.SESSIONKEY_MAXLEN);
            reader.ReadBytes(2);
            AccountType = reader.Read<MediusAccountType>();
            AccountName = reader.ReadString(MediusConstants.ACCOUNTNAME_MAXLEN);
            Password = reader.ReadString(MediusConstants.PASSWORD_MAXLEN);
        }

        public override void Serialize(BinaryWriter writer)
        {
            // 
            base.Serialize(writer);

            // 
            writer.Write(SessionKey, MediusConstants.SESSIONKEY_MAXLEN);
            writer.Write(new byte[2]);
            writer.Write(AccountType);
            writer.Write(AccountName, MediusConstants.ACCOUNTNAME_MAXLEN);
            writer.Write(Password, MediusConstants.PASSWORD_MAXLEN);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
             $"SessionKey:{SessionKey}" + " " +
$"AccountType:{AccountType}" + " " +
$"AccountName:{AccountName}" + " " +
$"Password:{Password}";
        }
    }
}