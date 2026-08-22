using KTC.Poker.Protocol;
using NUnit.Framework;
using UnityEngine;

namespace KTC.Poker.Tests
{
    /// <summary>
    /// プロトコルDTOが JsonUtility で往復できることの検証。
    /// これが通る = 生徒のRemoteGameSession (JSON over WebSocket) でそのまま使える。
    /// </summary>
    public class ProtocolSerializationTests
    {
        [Test]
        public void TableStateMessageはJSONで往復できる()
        {
            var original = new TableStateMessage
            {
                handNumber = 3,
                street = 1,
                communityCards = new byte[] { 14, 30, 50 },
                pot = 120,
                currentBet = 40,
                currentSeat = 2,
                buttonSeat = 1,
                smallBlindSeat = 2,
                bigBlindSeat = 3,
                yourSeat = 0,
                isYourTurn = true,
                actionRequest = new ActionRequestMessage
                {
                    canCall = true,
                    callAmount = 40,
                    canRaise = true,
                    minRaiseTo = 80,
                    maxRaiseTo = 200,
                },
                seats = new[]
                {
                    new SeatStateMessage { seat = 0, stack = 160, holeCards = new byte[] { 14, 30 } },
                    new SeatStateMessage { seat = 1, stack = 80, folded = true, holeCards = new byte[] { 0, 0 } },
                },
            };

            string json = JsonUtility.ToJson(original);
            var restored = JsonUtility.FromJson<TableStateMessage>(json);

            Assert.That(restored.handNumber, Is.EqualTo(3));
            Assert.That(restored.communityCards, Is.EqualTo(new byte[] { 14, 30, 50 }));
            Assert.That(restored.isYourTurn, Is.True);
            Assert.That(restored.actionRequest.minRaiseTo, Is.EqualTo(80));
            Assert.That(restored.seats.Length, Is.EqualTo(2));
            Assert.That(restored.seats[0].holeCards, Is.EqualTo(new byte[] { 14, 30 }));
            Assert.That(restored.seats[1].folded, Is.True);
        }

        [Test]
        public void 封筒による二重エンコードが往復できる()
        {
            var action = new PlayerActionMessage { actionType = 3, amount = 40 };
            var envelope = new GameMessageEnvelope
            {
                type = MessageTypes.PLAYER_ACTION,
                payload = JsonUtility.ToJson(action),
            };

            string wire = JsonUtility.ToJson(envelope);
            var receivedEnvelope = JsonUtility.FromJson<GameMessageEnvelope>(wire);
            Assert.That(receivedEnvelope.type, Is.EqualTo("playerAction"));

            var receivedAction = JsonUtility.FromJson<PlayerActionMessage>(receivedEnvelope.payload);
            Assert.That(receivedAction.actionType, Is.EqualTo(3));
            Assert.That(receivedAction.amount, Is.EqualTo(40));
        }
    }
}
