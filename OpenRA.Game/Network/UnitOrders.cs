#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System.Drawing;
using System.Linq;
using OpenRA.Traits;
using System;

namespace OpenRA.Network
{
	static class UnitOrders
	{
		static Player FindPlayerByClient(this World world, Session.Client c)
		{
			/* todo: this is still a hack. 
			 * the cases we're trying to avoid are the extra players on the host's client -- Neutral, other MapPlayers,
			 * bots,.. */
			return world.players.Values.FirstOrDefault(
				p => p.ClientIndex == c.Index && p.PlayerName == c.Name);
		}

		public static void ProcessOrder(OrderManager orderManager, World world, int clientId, Order order)
		{
			if (world != null)
			{
				if (!world.WorldActor.TraitsImplementing<IValidateOrder>().All(vo =>
					vo.OrderValidation(orderManager, world, clientId, order)))
					return;
			}

			switch (order.OrderString)
			{
			case "Chat":
					{
						var client = orderManager.LobbyInfo.ClientWithIndex(clientId);
						if (client != null)
						{
							var player = world != null ? world.FindPlayerByClient(client) : null;
							var suffix = (player != null && player.WinState == WinState.Lost) ? " (Dead)" : "";
							Game.AddChatLine(client.Color1, client.Name + suffix, order.TargetString);
						}
						else
							Game.AddChatLine(Color.White, "(player {0})".F(clientId), order.TargetString);
						break;
					}
				case "Disconnected": /* reports that the target player disconnected */
					{
						var client = orderManager.LobbyInfo.ClientWithIndex(clientId);
						if (client != null)
						{
							client.State = Session.ClientState.Disconnected;
						}
						break;
					}
				case "TeamChat":
					{
						var client = orderManager.LobbyInfo.ClientWithIndex(clientId);

						if (client != null)
						{
							if (world == null)
							{
								if (client.Team == orderManager.LocalClient.Team)
									Game.AddChatLine(client.Color1, client.Name + " (Team)",
													 order.TargetString);
							}
							else
							{
								var player = world.FindPlayerByClient(client);
								var display = player != null
											  &&
											  (world.LocalPlayer != null &&
											   player.Stances[world.LocalPlayer] == Stance.Ally
											   || player.WinState == WinState.Lost);

								if (display)
								{
									var suffix = (player != null && player.WinState == WinState.Lost)
													 ? " (Dead)"
													 : " (Team)";
									Game.AddChatLine(client.Color1, client.Name + suffix, order.TargetString);
								}
							}
						}
						break;
					}
				case "StartGame":
					{
						Game.AddChatLine(Color.White, "Server", "The game has started.");
						Game.StartGame(orderManager.LobbyInfo.GlobalSettings.Map);
						break;
					}
				
				case "HandshakeRequest":
				{
					Console.WriteLine("Client: Recieved HandshakeRequest");
					// Check valid mods/versions
					var serverInfo = Session.Deserialize(order.TargetString);
					var serverMods = serverInfo.GlobalSettings.Mods;
					var localMods = orderManager.LobbyInfo.GlobalSettings.Mods;
				
					// TODO: Check that the map exists on the client
					
					// Todo: Display a friendly dialog
					if (serverMods.SymmetricDifference(localMods).Count() > 0)
						throw new InvalidOperationException("Version mismatch. Client: `{0}`, Server: `{1}`"
					                                    .F(string.Join(",",localMods), string.Join(",",serverMods)));
					
					var response = new HandshakeResponse()
					{
						Name = "Test Player",
						Color1 = Color.PaleGreen,
						Color2 = Color.PeachPuff,
						Mods = localMods,
						Password = "Foo"
					};
					orderManager.IssueOrder(Order.HandshakeResponse(response.Serialize()));
					break;
				}
				
				case "SyncInfo":
					{
						Console.WriteLine("Client: Recieved SyncInfo");
						orderManager.LobbyInfo = Session.Deserialize(order.TargetString);

						if (orderManager.FramesAhead != orderManager.LobbyInfo.GlobalSettings.OrderLatency
							&& !orderManager.GameStarted)
						{
							orderManager.FramesAhead = orderManager.LobbyInfo.GlobalSettings.OrderLatency;
							Game.Debug(
								"Order lag is now {0} frames.".F(orderManager.LobbyInfo.GlobalSettings.OrderLatency));
						}
						Game.SyncLobbyInfo();
						break;
					}

				case "SetStance":
					{
						var targetPlayer = order.Player.World.players[order.TargetLocation.X];
						var newStance = (Stance)order.TargetLocation.Y;

						SetPlayerStance(world, order.Player, targetPlayer, newStance);

						Game.Debug("{0} has set diplomatic stance vs {1} to {2}".F(
							order.Player.PlayerName, targetPlayer.PlayerName, newStance));
				
						// automatically declare war reciprocally
						if (newStance == Stance.Enemy && targetPlayer.Stances[order.Player] == Stance.Ally)
						{
							SetPlayerStance(world, targetPlayer, order.Player, newStance);
							Game.Debug("{0} has reciprocated",targetPlayer.PlayerName);
						}

						break;
					}
				default:
					{
						if( !order.IsImmediate )
						{
							var self = order.Subject;
							var health = self.TraitOrDefault<Health>();
							if( health == null || !health.IsDead )
								foreach( var t in self.TraitsImplementing<IResolveOrder>() )
									t.ResolveOrder( self, order );
						}
						break;
					}
			}
		}

		static void SetPlayerStance(World w, Player p, Player target, Stance s)
		{
			var oldStance = p.Stances[target];
			p.Stances[target] = s;
			if (target == w.LocalPlayer)
				w.WorldActor.Trait<Shroud>().UpdatePlayerStance(w, p, oldStance, s);
		}
	}
}
