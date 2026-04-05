using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures; // 添加缺失的命名空间
using TerrariaApi.Server;
using TShockAPI;

namespace PlayerOriginBuilder
{
    [ApiVersion(2, 1)]
    public class PlayerBuilder : TerrariaPlugin
    {
        public override string Name => "玩家原点建造插件";
        public override string Author => "淦";
        public override string Description => "以玩家为原点放置物块";
        public override Version Version => new(2025, 6, 28, 1);

        public PlayerBuilder(Main game) : base(game) { }

        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command(
                permissions: new List<string> { "playerbuilder.use" },
                cmd: PlaceBlockCommand,
                "placeatplayer", "pp"
            ));
            
            Commands.ChatCommands.Add(new Command(
                permissions: new List<string> { "playerbuilder.use" },
                cmd: ListPlayersCommand,
                "listplayers", "lp"
            ));
        }

        private void PlaceBlockCommand(CommandArgs args)
        {
            if (!args.Player.HasPermission("playerbuilder.use"))
            {
                args.Player.SendErrorMessage("你没有使用该指令的权限!");
                return;
            }

            if (args.Parameters.Count < 4)
            {
                args.Player.SendErrorMessage("用法: /placeatplayer <玩家名称> <X偏移> <Y偏移> <物块ID> [物块样式]");
                args.Player.SendErrorMessage("示例: /pp 张三 5 -3 1     # 在张三上方5格右侧3格放置石块");
                args.Player.SendErrorMessage("示例: /pp all 0 0 30      # 在所有玩家位置放置火把");
                args.Player.SendErrorMessage("示例: /pp admin 10 0 4 2  # 在管理员右侧10格放置蓝玉块");
                args.Player.SendErrorMessage("使用 /listplayers 查看当前在线玩家");
                return;
            }

            // 解析玩家标识符
            string playerIdentifier = args.Parameters[0].ToLower();
            
            // 解析偏移坐标
            if (!int.TryParse(args.Parameters[1], out int offsetX))
            {
                args.Player.SendErrorMessage("X偏移必须是整数!");
                return;
            }
            
            if (!int.TryParse(args.Parameters[2], out int offsetY))
            {
                args.Player.SendErrorMessage("Y偏移必须是整数!");
                return;
            }
            
            // 解析物块ID
            if (!int.TryParse(args.Parameters[3], out int tileId) || tileId < 0 || tileId >= TileID.Count)
            {
                args.Player.SendErrorMessage($"物块ID必须是0-{TileID.Count - 1}之间的整数!");
                return;
            }
            
            // 解析物块样式（可选）
            int style = 0;
            if (args.Parameters.Count > 4 && !int.TryParse(args.Parameters[4], out style))
            {
                args.Player.SendErrorMessage("物块样式必须是整数!");
                return;
            }

            // 查找目标玩家
            List<TSPlayer> targetPlayers = FindTargetPlayers(playerIdentifier);
            
            if (targetPlayers.Count == 0)
            {
                args.Player.SendErrorMessage($"未找到匹配的玩家: {playerIdentifier}");
                return;
            }

            int placedBlocks = 0;
            
            foreach (var player in targetPlayers)
            {
                // 计算玩家位置（像素坐标转物块坐标）
                int playerTileX = (int)(player.X / 16);
                int playerTileY = (int)(player.Y / 16);
                
                // 计算目标位置
                int targetX = playerTileX + offsetX;
                int targetY = playerTileY + offsetY;
                
                // 验证位置是否有效
                if (!IsValidTilePosition(targetX, targetY))
                {
                    continue;
                }
                
                // 清除原有物块（确保可以放置新物块）
                WorldGen.KillTile(targetX, targetY, noItem: true);
                
                // 放置物块 - 使用更可靠的方法
                bool placed = PlaceTileSafely(targetX, targetY, tileId, style);
                
                if (placed)
                {
                    placedBlocks++;
                    
                    // 更新客户端 - 发送更大范围的更新确保同步
                    TSPlayer.All.SendTileSquare(targetX, targetY, 3);
                    
                    // 通知玩家
                    player.SendSuccessMessage($"在你的位置({targetX},{targetY})放置了物块");
                }
            }
            
            string tileName = GetTileName(tileId);
            string playerList = string.Join(", ", targetPlayers.Select(p => p.Name));
            args.Player.SendSuccessMessage($"在 {playerList} 周围放置了 {placedBlocks} 个{tileName}");
        }
        
        // 查找目标玩家
        private List<TSPlayer> FindTargetPlayers(string identifier)
        {
            if (identifier == "all")
            {
                return TShock.Players
                    .Where(p => p != null && p.Active)
                    .ToList();
            }
            
            // 尝试按部分名称匹配
            var matchedPlayers = new List<TSPlayer>();
            string lowerIdentifier = identifier.ToLower();
            
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active && player.Name != null)
                {
                    if (player.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        // 精确匹配
                        return new List<TSPlayer> { player };
                    }
                    
                    if (player.Name.ToLower().Contains(lowerIdentifier))
                    {
                        matchedPlayers.Add(player);
                    }
                }
            }
            
            return matchedPlayers;
        }
        
        private bool IsValidTilePosition(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && 
                   y >= 0 && y < Main.maxTilesY;
        }
        
        private bool PlaceTileSafely(int x, int y, int tileId, int style)
        {
            try
            {
                // 尝试使用PlaceTile放置物块
                bool placed = WorldGen.PlaceTile(x, y, tileId, style: style);
                
                // 如果PlaceTile失败，直接设置物块
                if (!placed)
                {
                    WorldGen.PlaceTile(x, y, tileId, forced: true);
                    
                    // 设置物块激活状态
                    Main.tile[x, y].active(true);
                    Main.tile[x, y].type = (ushort)tileId;
                    
                    // 设置物块帧（样式）- 使用更兼容的方法
                    SetTileFrame(x, y, tileId, style);
                }
                
                // 确保物块被正确设置
                if (Main.tile[x, y].type != tileId)
                {
                    Main.tile[x, y].type = (ushort)tileId;
                    Main.tile[x, y].active(true);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        // 兼容的帧设置方法
        private void SetTileFrame(int x, int y, int tileId, int style)
        {
            // 仅当样式大于0时设置帧
            if (style <= 0) return;
            
            // 对于有样式的物块，设置适当的帧
            switch (tileId)
            {
                case TileID.Torches:
                    Main.tile[x, y].frameX = (short)(style * 18);
                    break;
                case TileID.Containers: // 箱子
                case TileID.Containers2: // 其他容器
                    Main.tile[x, y].frameX = (short)(style * 36);
                    break;
                case TileID.Platforms:
                    Main.tile[x, y].frameX = (short)(style * 18);
                    break;
                case TileID.Pots:
                    Main.tile[x, y].frameX = (short)(style * 18);
                    break;
                default:
                    // 通用方法：尝试设置样式
                    Main.tile[x, y].frameX = (short)(style * 18);
                    break;
            }
        }

        // 列出在线玩家
        private void ListPlayersCommand(CommandArgs args)
        {
            var onlinePlayers = TShock.Players
                .Where(p => p != null && p.Active)
                .ToList();
            
            if (onlinePlayers.Count == 0)
            {
                args.Player.SendInfoMessage("当前没有在线玩家");
                return;
            }
            
            args.Player.SendInfoMessage("===== 在线玩家列表 =====");
            foreach (var player in onlinePlayers)
            {
                args.Player.SendInfoMessage($"{player.Name} - 位置: ({player.TileX}, {player.TileY})");
            }
            args.Player.SendInfoMessage("使用 /placeatplayer <玩家名称> <X偏移> <Y偏移> <物块ID> 放置物块");
        }
        
        private string GetTileName(int tileId)
        {
            // 常见物块名称映射
            var tileNames = new Dictionary<int, string>
            {
                {TileID.Dirt, "泥土块"},
                {TileID.Stone, "石块"},
                {TileID.Grass, "草块"},
                {TileID.Torches, "火把"},
                {TileID.WoodBlock, "木材"},
                {TileID.Iron, "铁块"},
                {TileID.Gold, "金块"},
                {TileID.Silver, "银块"},
                {TileID.Copper, "铜块"},
                {TileID.Sand, "沙块"},
                {TileID.Glass, "玻璃"},
                {TileID.Platforms, "平台"},
                {TileID.GoldBrick, "金砖"},
                {TileID.Diamond, "钻石块"},
                {TileID.Ruby, "红玉块"},
                {TileID.Emerald, "翡翠块"},
                {TileID.Sapphire, "蓝玉块"},
                {TileID.Topaz, "黄玉块"},
                {TileID.Amethyst, "紫晶块"},
                {TileID.Meteorite, "陨石块"},
                {TileID.Obsidian, "黑曜石"},
                {TileID.Hellstone, "狱石"},
                {TileID.Pearlstone, "珍珠石"},
                {TileID.Ebonstone, "黑檀石"},
                {TileID.Crimstone, "猩红石"},
                {TileID.Mud, "泥块"},
                {TileID.JungleGrass, "丛林草"},
                {TileID.SnowBlock, "雪块"},
                {TileID.IceBlock, "冰块"},
                {TileID.Cobalt, "钴矿"},
                {TileID.Mythril, "秘银矿"},
                {TileID.Adamantite, "精金矿"},
                {TileID.DemonAltar, "恶魔祭坛"},
                {TileID.Containers, "箱子"} // 修复：使用正确的TileID
            };
            
            return tileNames.TryGetValue(tileId, out string name) ? name : $"物块ID:{tileId}";
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}