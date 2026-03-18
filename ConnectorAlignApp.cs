
/*
    Connector Alignment App for Space Engineers

    Copyright (C) 2026 Philipp Decker - kiminaze@yahoo.de

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ModAPI;
using VRageMath;

namespace SEConnectorAlignApp
{
    [MyTextSurfaceScript("ConnectorAlignApp", "Connector Alignment")]
    public class ConnectorAlignApp : MyTSSCommon
    {
        // update rate
        public override ScriptUpdate NeedsUpdate { get; } = ScriptUpdate.Update10;

        // grid/block stuff
        readonly IMyTerminalBlock block;
        readonly IMyCubeGrid grid;
        readonly List<IMyCubeGrid> mainGrids = new List<IMyCubeGrid>();

        // logic stuff
        IMyShipConnector gridConnector;
        IMyShipConnector targetConnector;

        // user changeable stuff
        readonly MyIni customDataIni = new MyIni();
        const int CUSTOMDATA_UPDATE_RATE = 12;
        int customDataTick = 0;
        const int COLOR_UPDATE_RATE = 12;
        int colorTick = 6; // offset from custom data

        bool relativeToLCD = true;
        bool useLargeFont = true;
        string customConnectorName = "";

        // visual stuff
        RectangleF screen;
        readonly float aspectRatio;
        readonly float invAspectRatio;
        readonly float minScreenExtend;

        MySprite midPoint;
        MySprite leftRing;
        MySprite rightRing;
        MySprite topRing;
        MySprite bottomRing;

        MySprite gridConnectorLabel;
        MySprite targetConnectorLabel;

        MySprite positionOffsetLabel;
        MySprite positionOffsetIndicator;
        MySprite pitchLabel;
        MySprite pitchIndicator;
        MySprite yawLabel;
        MySprite yawIndicator;
        MySprite rollLabel;
        MySprite rollIndicator;
        MySprite velocityDiffIndicator;

        MySprite targetConnectorSprite;

        Color foregroundColor;

        public ConnectorAlignApp(IMyTextSurface surface, IMyCubeBlock cubeBlock, Vector2 size) : base(surface, cubeBlock, size)
        {
            block = (IMyTerminalBlock)cubeBlock;
            block.OnMarkForClose += BlockDeleted;

            grid = block.CubeGrid;

            MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, mainGrids);

            // handle config
            GetOrCreateConfig();

            // get screen values
            screen = new RectangleF((Surface.TextureSize - Surface.SurfaceSize) * 0.5f, Surface.SurfaceSize);
            aspectRatio = screen.Width / screen.Height;
            invAspectRatio = 1f / aspectRatio;
            minScreenExtend = screen.Width > screen.Height ? screen.Height : screen.Width;

            foregroundColor = Surface.ScriptForegroundColor;

            // create all sprites
            CreateSprites();
        }

        public override void Run()
        {
            base.Run();

            // check for custom data changes
            CheckCustomData();

            // check for changes in foreground color
            CheckAndFixColors();

            // check for valid connector pair and populate grid- and targetConnector
            FindCloseConnectors();

            if (gridConnector != null)
            {
                // get connector offsets
                Vector3D connOffset, connRotationOffset, lcdOffset, lcdRotationOffset;
                GetConnectorOffset(out connOffset, out connRotationOffset, out lcdOffset, out lcdRotationOffset);

                // set UI values
                gridConnectorLabel.Data = WrapText(gridConnector.CustomName, 12);
                targetConnectorLabel.Data = WrapText(targetConnector.CustomName, 12);

                targetConnectorSprite.Position = NormalizedToScreenPosition((float)lcdOffset.X * 0.025f, -(float)lcdOffset.Y * 0.025f, 0.5f, 0.5f, true);
                targetConnectorSprite.Size = TextureSizeToScreenSize(0.1f, 51f / 51f) * (1f + (float)lcdOffset.Z * 0.0125f);
                targetConnectorSprite.RotationOrScale = (float)MathHelper.ToRadians(connRotationOffset.Z);

                if (gridConnector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connectable)
                    positionOffsetIndicator.Color = Color.Yellow;
                else if (gridConnector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
                    positionOffsetIndicator.Color = Color.Green;
                else
                    positionOffsetIndicator.Color = Surface.ScriptForegroundColor;

                if (relativeToLCD)
                {
                    positionOffsetIndicator.Data = string.Format(useLargeFont ? "{0}\n{1}\n{2}"  : "{0} m\n{1} m\n{2} m", Math.Round(lcdOffset.X, 2), Math.Round(lcdOffset.Y, 2), Math.Round(lcdOffset.Z, 2));

                    pitchIndicator.Data = $"{Math.Round(lcdRotationOffset.X, 1)}°";
                    yawIndicator.Data = $"{Math.Round(lcdRotationOffset.Y, 1)}°";
                    rollIndicator.Data = $"{Math.Round(lcdRotationOffset.Z, 1)}°";
                }
                else
                {
                    positionOffsetIndicator.Data = string.Format(useLargeFont ? "{0}\n{1}\n{2}" : "{0} m\n{1} m\n{2} m", Math.Round(connOffset.X, 2), Math.Round(connOffset.Y, 2), Math.Round(connOffset.Z, 2));

                    pitchIndicator.Data = $"{Math.Round(connRotationOffset.X, 1)}°";
                    yawIndicator.Data = $"{Math.Round(connRotationOffset.Y, 1)}°";
                    rollIndicator.Data = $"{Math.Round(connRotationOffset.Z, 1)}°";
                }

                velocityDiffIndicator.Data = $"{Math.Round(targetConnector.CubeGrid.LinearVelocity.Length() - gridConnector.CubeGrid.LinearVelocity.Length(), 1)} m/s";
            }
            else
            {
                gridConnectorLabel.Data = " - ";
                targetConnectorLabel.Data = " - ";
                positionOffsetIndicator.Data = useLargeFont ? "0\n0\n0" : "0 m\n0 m\n0 m";
                positionOffsetIndicator.Color = Surface.ScriptForegroundColor;

                pitchIndicator.Data = "0°";
                yawIndicator.Data = "0°";
                rollIndicator.Data = "0°";

                velocityDiffIndicator.Data = "0 m/s";
            }

            // draw to screen
            using (var frame = Surface.DrawFrame())
            {
                // sprites
                frame.Add(midPoint);
                frame.Add(leftRing);
                frame.Add(topRing);
                frame.Add(rightRing);
                frame.Add(bottomRing);

                if (targetConnector != null)
                    frame.Add(targetConnectorSprite);

                // texts
                frame.Add(gridConnectorLabel);
                frame.Add(targetConnectorLabel);

                frame.Add(positionOffsetLabel);
                frame.Add(positionOffsetIndicator);

                frame.Add(pitchLabel);
                frame.Add(pitchIndicator);
                frame.Add(yawLabel);
                frame.Add(yawIndicator);
                frame.Add(rollLabel);
                frame.Add(rollIndicator);

                frame.Add(velocityDiffIndicator);
            }
        }

        /// <summary>
        /// Creates all sprites
        /// </summary>
        private void CreateSprites()
        {
            float valuesFontSize = GetFontSize(useLargeFont ? 0.075f : 0.033f);

            midPoint = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_VelocityVector",
                Position = NormalizedToScreenPosition(0.0f, 0.0f, 0.5f, 0.5f, true),
                RotationOrScale = 0.0f,
                Color = Surface.ScriptForegroundColor.Alpha(0.66f),
                Alignment = TextAlignment.CENTER,
                Size = TextureSizeToScreenSize(0.1f, 51f / 51f)
            };

            leftRing = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "LCD_CAA_DOCKING_BRACKET",
                Position = NormalizedToScreenPosition(-0.35f, 0.0f, 0.5f, 0.5f, true),
                RotationOrScale = 0.0f,
                Color = Surface.ScriptForegroundColor.Alpha(0.66f),
                Alignment = TextAlignment.CENTER,
                Size = TextureSizeToScreenSize(0.4f, 128f / 512f)
            };
            topRing = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "LCD_CAA_DOCKING_BRACKET",
                Position = NormalizedToScreenPosition(0.0f, -0.35f, 0.5f, 0.5f, true),
                RotationOrScale = MathHelper.ToRadians(90f),
                Color = Surface.ScriptForegroundColor.Alpha(0.66f),
                Alignment = TextAlignment.CENTER,
                Size = TextureSizeToScreenSize(0.4f, 128f / 512f)
            };
            rightRing = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "LCD_CAA_DOCKING_BRACKET",
                Position = NormalizedToScreenPosition(0.35f, 0.0f, 0.5f, 0.5f, true),
                RotationOrScale = MathHelper.ToRadians(180f),
                Color = Surface.ScriptForegroundColor.Alpha(0.66f),
                Alignment = TextAlignment.CENTER,
                Size = TextureSizeToScreenSize(0.4f, 128f / 512f)
            };
            bottomRing = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "LCD_CAA_DOCKING_BRACKET",
                Position = NormalizedToScreenPosition(0.0f, 0.35f, 0.5f, 0.5f, true),
                RotationOrScale = MathHelper.ToRadians(270f),
                Color = Surface.ScriptForegroundColor.Alpha(0.66f),
                Alignment = TextAlignment.CENTER,
                Size = TextureSizeToScreenSize(0.4f, 128f / 512f)
            };

            targetConnectorSprite = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_VelocityVector",
                Position = screen.Center,
                RotationOrScale = 0.0f,
                Color = Surface.ScriptForegroundColor.Alpha(0.66f),
                Alignment = TextAlignment.CENTER,
                Size = new Vector2(51, 51) * 0.5f
            };

            gridConnectorLabel = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = " - ",
                Position = NormalizedToScreenPosition(-0.475f, -0.475f, 0.5f, 0.5f, true),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = MyFontEnum.White
            };
            targetConnectorLabel = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = " - ",
                Position = NormalizedToScreenPosition(0.475f, -0.475f, 0.5f, 0.5f, true),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.RIGHT,
                FontId = MyFontEnum.White
            };

            positionOffsetLabel = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "X\nY\nZ",
                Position = NormalizedToScreenPosition(useLargeFont ? -0.415f : -0.385f, 0.0f, 0.5f, 0.5f, true) + new Vector2(0.0f, -valuesFontSize * 1.5f * 28.8f),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };
            positionOffsetIndicator = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = useLargeFont ? "0\n0\n0" : "0 m\n0 m\n0 m",
                Position = NormalizedToScreenPosition(-0.35f, 0.0f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 1.5f * 28.8f),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = MyFontEnum.White
            };

            pitchLabel = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "P\nI\nT\nC\nH",
                Position = NormalizedToScreenPosition(useLargeFont ? 0.415f : 0.38f, 0.0f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 0.65f * 2.5f * 28.8f),
                RotationOrScale = valuesFontSize * 0.65f,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };
            pitchIndicator = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "0°",
                Position = NormalizedToScreenPosition(0.35f, 0.0f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 0.5f * 28.8f),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.RIGHT,
                FontId = MyFontEnum.White
            };
            yawLabel = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "YAW",
                Position = NormalizedToScreenPosition(0.0f, useLargeFont ? 0.475f : 0.4f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 28.8f),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };
            yawIndicator = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "0°",
                Position = NormalizedToScreenPosition(0.0f, 0.36f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 28.8f),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };
            rollLabel = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "ROLL",
                Position = NormalizedToScreenPosition(0.0f, useLargeFont ? -0.475f : -0.4f, 0.5f, 0.5f, true),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };
            rollIndicator = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "0°",
                Position = NormalizedToScreenPosition(0.0f, -0.36f, 0.5f, 0.5f, true),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };

            velocityDiffIndicator = new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = "0 m/s",
                Position = NormalizedToScreenPosition(0.25f, 0.25f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 28.8f * 0.5f),
                RotationOrScale = valuesFontSize,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = MyFontEnum.White
            };
        }

        /// <summary>
        /// Gets config from block custom data (or creates it instead).
        /// </summary>
        private void GetOrCreateConfig()
        {
            MyIniParseResult result;
            customDataIni.TryParse(block.CustomData, out result);

            bool updateCustomData = false;

            if (!customDataIni.ContainsSection("ConnAlign"))
            {
                customDataIni.AddSection("ConnAlign");
                updateCustomData = true;
            }

            if (!customDataIni.ContainsKey("ConnAlign", "relativeToLcd"))
            {
                customDataIni.Set("ConnAlign", "relativeToLcd", true);
                updateCustomData = true;
            }
            else
            {
                relativeToLCD = customDataIni.Get("ConnAlign", "relativeToLcd").ToBoolean(true);
            }

            if (!customDataIni.ContainsKey("ConnAlign", "useLargeFont"))
            {
                customDataIni.Set("ConnAlign", "useLargeFont", true);
                updateCustomData = true;
            }
            else
            {
                useLargeFont = customDataIni.Get("ConnAlign", "useLargeFont").ToBoolean(true);
            }

            if (!customDataIni.ContainsKey("ConnAlign", "connectorName"))
            {
                customDataIni.Set("ConnAlign", "connectorName", "");
                updateCustomData = true;
            }
            else
            {
                customConnectorName = customDataIni.Get("ConnAlign", "connectorName").ToString("");
            }

            if (updateCustomData)
                block.CustomData = customDataIni.ToString();
        }

        /// <summary>
        /// Check custom data and set values accordingly. (happens every ~2 seconds)
        /// </summary>
        private void CheckCustomData()
        {
            customDataTick++;
            if (customDataTick < CUSTOMDATA_UPDATE_RATE)
                return;

            customDataTick = 0;

            MyIniParseResult result;
            if (!customDataIni.TryParse(block.CustomData, out result))
                return;

            relativeToLCD = customDataIni.Get("ConnAlign", "relativeToLcd").ToBoolean(true);

            bool _useLargeFont = customDataIni.Get("ConnAlign", "useLargeFont").ToBoolean(true);
            if (_useLargeFont != useLargeFont)
            {
                useLargeFont = _useLargeFont;

                float valuesFontSize = GetFontSize(0.033f);
                if (useLargeFont)
                    valuesFontSize = GetFontSize(0.075f);

                gridConnectorLabel.RotationOrScale = valuesFontSize;
                targetConnectorLabel.RotationOrScale = valuesFontSize;
                positionOffsetLabel.RotationOrScale = valuesFontSize;
                positionOffsetLabel.Position = NormalizedToScreenPosition(useLargeFont ? -0.415f : -0.385f, 0.0f, 0.5f, 0.5f, true) + new Vector2(0.0f, -valuesFontSize * 1.5f * 28.8f);
                positionOffsetIndicator.RotationOrScale = valuesFontSize;
                positionOffsetIndicator.Position = NormalizedToScreenPosition(-0.35f, 0.0f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 1.5f * 28.8f);
                positionOffsetIndicator.Data = useLargeFont ? "0\n0\n0" : "0 m\n0 m\n0 m";
                pitchLabel.RotationOrScale = valuesFontSize * 0.65f;
                pitchLabel.Position = NormalizedToScreenPosition(useLargeFont ? 0.415f : 0.38f, 0.0f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 0.65f * 2.5f * 28.8f);
                pitchIndicator.RotationOrScale = valuesFontSize;
                pitchIndicator.Position = NormalizedToScreenPosition(0.35f, 0.0f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 0.5f * 28.8f);
                yawLabel.RotationOrScale = valuesFontSize;
                yawLabel.Position = NormalizedToScreenPosition(0.0f, useLargeFont ? 0.475f : 0.4f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 28.8f);
                yawIndicator.RotationOrScale = valuesFontSize;
                yawIndicator.Position = NormalizedToScreenPosition(0.0f, 0.36f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 28.8f);
                rollLabel.RotationOrScale = valuesFontSize;
                rollLabel.Position = NormalizedToScreenPosition(0.0f, useLargeFont ? -0.475f : -0.4f, 0.5f, 0.5f, true);
                rollIndicator.RotationOrScale = valuesFontSize;
                velocityDiffIndicator.RotationOrScale = valuesFontSize;
                velocityDiffIndicator.Position = NormalizedToScreenPosition(0.25f, 0.25f, 0.5f, 0.5f, true) - new Vector2(0.0f, valuesFontSize * 28.8f * 0.5f);
            }

            customConnectorName = customDataIni.Get("ConnAlign", "connectorName").ToString("");
        }

        /// <summary>
        /// Checks for changes in LCD colors and changes sprites accordingly.
        /// </summary>
        private void CheckAndFixColors()
        {
            colorTick++;
            if (colorTick < COLOR_UPDATE_RATE)
                return;

            colorTick = 0;

            if (Surface.ScriptForegroundColor == foregroundColor)
                return;

            foregroundColor = Surface.ScriptForegroundColor;

            midPoint.Color = foregroundColor;
            leftRing.Color = foregroundColor;
            topRing.Color = foregroundColor;
            rightRing.Color = foregroundColor;
            bottomRing.Color = foregroundColor;

            targetConnectorSprite.Color = foregroundColor;

            gridConnectorLabel.Color = foregroundColor;
            targetConnectorLabel.Color = foregroundColor;

            positionOffsetLabel.Color = foregroundColor;
            positionOffsetIndicator.Color = foregroundColor;

            pitchLabel.Color = foregroundColor;
            pitchIndicator.Color = foregroundColor;
            yawLabel.Color = foregroundColor;
            yawIndicator.Color = foregroundColor;
            rollLabel.Color = foregroundColor;
            rollIndicator.Color = foregroundColor;

            velocityDiffIndicator.Color = foregroundColor;
        }

        /// <summary>
        /// Gets all connectors on the specified grid. Takes customConnectorName into account.
        /// </summary>
        /// <param name="cubeGrid"></param>
        /// <returns></returns>
        private List<IMyShipConnector> GetConnectorsOnGrid(IMyCubeGrid cubeGrid)
        {
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(blocks, b => {
                if (customConnectorName == "")
                    return b?.FatBlock is IMyShipConnector;
                else
                    return b?.FatBlock is IMyShipConnector && (b?.FatBlock as IMyShipConnector).CustomName == customConnectorName;
            });

            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            foreach (IMySlimBlock slimBlock in blocks)
            {
                connectors.Add(slimBlock.FatBlock as IMyShipConnector);
            }
            return connectors;
        }

        /// <summary>
        /// Returns a list of all connectors on the grid and (mechanically connected) subgrids.
        /// </summary>
        private List<IMyShipConnector> GetConnectorsInGridGroup()
        {
            List<IMyShipConnector> connectors = new List<IMyShipConnector>();

            foreach (IMyCubeGrid subGrid in mainGrids)
                connectors.AddRange(GetConnectorsOnGrid(subGrid));

            return connectors;
        }

        /// <summary>
        /// Checks for valid connector pair.
        /// </summary>
        private void FindCloseConnectors()
        {
            gridConnector = null;
            targetConnector = null;
            double distance = 1000;

            List<IMyShipConnector> gridConnectors = GetConnectorsInGridGroup();
            foreach (IMyShipConnector tmpGridConnector in gridConnectors)
            {
                bool isSmallGridConn;
                bool isGridConn = IsConnector(tmpGridConnector, out isSmallGridConn);

                if (!isGridConn)
                    continue;

                Vector3D forwardPosition = tmpGridConnector.GetPosition() + tmpGridConnector.WorldMatrix.Forward * 20f;
                MyOrientedBoundingBoxD box = new MyOrientedBoundingBoxD(forwardPosition, new Vector3D(20f), Quaternion.CreateFromRotationMatrix(tmpGridConnector.WorldMatrix));
                BoundingSphereD sphere = new BoundingSphereD(forwardPosition, 100.0);
                List<IMyEntity> entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere).Where(ent => {
                    if (ent is IMyShipConnector == false || ent == tmpGridConnector)
                        return false;

                    if (mainGrids.Contains((ent as IMyShipConnector).CubeGrid))
                        return false;

                    Vector3D pos = ent.GetPosition();
                    return box.Contains(ref pos);
                }).ToList();

                foreach (IMyEntity entity in entities)
                {
                    IMyShipConnector tmpTargetConnector = entity as IMyShipConnector;

                    if (gridConnectors.Contains(tmpTargetConnector))
                        continue;

                    bool isSmallTargetConn;
                    bool isTargetConn = IsConnector(tmpTargetConnector, out isSmallTargetConn);

                    if (!isTargetConn)
                        continue;
                    if ((isSmallGridConn && !isSmallTargetConn) || (!isSmallGridConn && isSmallTargetConn))
                        continue;
                    double tmpDistance = Vector3D.Distance(tmpTargetConnector.GetPosition(), tmpGridConnector.GetPosition());
                    if (tmpDistance > distance)
                        continue;

                    gridConnector = tmpGridConnector;
                    targetConnector = tmpTargetConnector;
                    distance = tmpDistance;
                }
            }
        }

        /// <summary>
        /// Get the connectors dummy matrix for correct offsets.
        /// </summary>
        /// <param name="connector"></param>
        /// <returns></returns>
        private MatrixD GetConnectorDummyMatrix(IMyShipConnector connector)
        {
            Dictionary<string, IMyModelDummy> modelDummies = new Dictionary<string, IMyModelDummy>();
            connector.Model.GetDummies(modelDummies);
            foreach (KeyValuePair<string, IMyModelDummy> kvp in modelDummies)
            {
                if (kvp.Key.ToLower().Contains("connector"))
                {
                    return kvp.Value.Matrix;
                }
            }

            return connector.WorldMatrix;
        }

        /// <summary>
        /// Get the positional and rotational offset including roll in relation to 90° increments.
        /// </summary>
        /// <param name="positionOffset">Positional offset related to world space.</param>
        /// <param name="rotationOffset">Rotational offset related to world space.</param>
        /// <param name="lcdPositionOffset">Positional offset related to the LCD.</param>
        /// <param name="lcdRotationOffset">Rotational offset related to the LCD.</param>
        private void GetConnectorOffset(out Vector3D positionOffset, out Vector3D rotationOffset, out Vector3D lcdPositionOffset, out Vector3D lcdRotationOffset)
        {
            MatrixD gridConnDummyM = GetConnectorDummyMatrix(gridConnector);
            MatrixD targetConnDummyM = GetConnectorDummyMatrix(targetConnector);

            MatrixD gridConnRotM = gridConnector.WorldMatrix;
            gridConnRotM.Translation = Vector3D.Zero;
            MatrixD targetConnRotM = targetConnector.WorldMatrix;
            targetConnRotM.Translation = Vector3D.Zero;
            MatrixD lcdRotM = block.WorldMatrix;
            lcdRotM.Translation = Vector3D.Zero;

            // world offset
            //todo: add offset from block middle to connection plane (use connector dummies?)
            MatrixD invGridConRotM = MatrixD.Transpose(gridConnRotM);
            positionOffset = Vector3D.Transform(targetConnector.WorldMatrix.Translation - gridConnector.WorldMatrix.Translation, invGridConRotM);

            // lcd offset
            MatrixD invLcdRotM = MatrixD.Transpose(lcdRotM);
            lcdPositionOffset = Vector3D.TransformNormal(targetConnector.GetPosition() - gridConnector.GetPosition(), invLcdRotM);

            // get correct orientation for angles
            //   180° flip to put forward vector in line
            targetConnRotM *= MatrixD.CreateFromAxisAngle(targetConnRotM.Up, Math.PI);

            //   get relative rotation matrix
            MatrixD relRotM = targetConnRotM * invGridConRotM;

            //   get eulers once
            Vector3D relativeRadians;
            MatrixD.GetEulerAnglesXYZ(ref relRotM, out relativeRadians);

            //   rotate roll to nearest 90°
            double rollOffsetTo90 = -Math.Round(MathHelper.ToDegrees(relativeRadians.Z) / 90.0);
            targetConnRotM *= MatrixD.CreateFromAxisAngle(targetConnRotM.Forward, (Math.PI / 2.0) * rollOffsetTo90);

            // get relative rotation matrix again
            relRotM = targetConnRotM * invGridConRotM;

            // get euler angles
            MatrixD.GetEulerAnglesXYZ(ref relRotM, out relativeRadians);
            rotationOffset = new Vector3D(
                MathHelper.ToDegrees(relativeRadians.X),
                MathHelper.ToDegrees(relativeRadians.Y),
                MathHelper.ToDegrees(relativeRadians.Z)
            );

            // get lcd rotation offset
            MatrixD localRelRot = gridConnRotM * invLcdRotM;

            Vector3D result;
            Vector3D.Rotate(ref rotationOffset, ref localRelRot, out result);

            lcdRotationOffset = result;
        }

        /// <summary>
        /// Checks if a connector actually has (small_)connector dummies.
        /// </summary>
        /// <param name="connector">The connector to check.</param>
        /// <param name="isSmall">If the connector is a small connector.</param>
        /// <returns>If it is a connector.</returns>
        private bool IsConnector(IMyShipConnector connector, out bool isSmall)
        {
            bool hasConnectorDummy = false;
            isSmall = false;
            Dictionary<string, IMyModelDummy> modelDummies = new Dictionary<string, IMyModelDummy>();
            connector.Model.GetDummies(modelDummies);
            foreach (KeyValuePair<string, IMyModelDummy> kvp in modelDummies)
            {
                if (kvp.Key.ToLower().Contains("connector"))
                    hasConnectorDummy = true;
                if (kvp.Key.ToLower().Contains("small_connector"))
                    isSmall = true;
            }

            return hasConnectorDummy;
        }

        /// <summary>
        /// Translates a normalized position (0.0-1.0) to screen position.
        /// </summary>
        /// <param name="x">X component of the normalized position.</param>
        /// <param name="y">Y component of the normalized position.</param>
        /// <param name="relativeX">Normalised relative X position.</param>
        /// <param name="relativeY">Normalised relative Y position.</param>
        /// <param name="useSquareAspectRatio"></param>
        /// <returns>Screen position</returns>
        private Vector2 NormalizedToScreenPosition(float x, float y, float relativeX = 0f, float relativeY = 0f, bool useSquareAspectRatio = false)
        {
            return new Vector2(
                (relativeX + x) * (useSquareAspectRatio ? minScreenExtend : screen.Width) + screen.X + (useSquareAspectRatio ? (screen.Width - minScreenExtend) * 0.5f : 0f),
                (relativeY + y) * (useSquareAspectRatio ? minScreenExtend : screen.Height) + screen.Y + (useSquareAspectRatio ? (screen.Height - minScreenExtend) * 0.5f : 0f)
            );
        }

        /// <summary>
        /// Recalculates size of a texture for a screen while respecting aspect ratio
        /// </summary>
        /// <param name="size">Size on screen (0.0-1.0)</param>
        /// <param name="textureAspectRatio">Aspect ratio for the texture</param>
        /// <returns>Size in screen coords</returns>
        private Vector2 TextureSizeToScreenSize(float size, float textureAspectRatio)
        {
            if (aspectRatio > 1f)
                return new Vector2(screen.Width * textureAspectRatio * invAspectRatio, screen.Height) * size;
            else
                return new Vector2(screen.Width * textureAspectRatio, screen.Height * aspectRatio) * size;
        }

        /// <summary>
        /// Gets font size for normalized height. (works for monospace and white font, idk about others)
        /// </summary>
        /// <param name="height"></param>
        /// <returns></returns>
        private float GetFontSize(float height)
        {
            return (height * minScreenExtend) / 28.8f;
        }

        /// <summary>
        /// Wrap text to a maximum width.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxWidth"></param>
        /// <returns></returns>
        private string WrapText(string text, int maxWidth)
        {
            string wrappedText = text.Trim();
            if (text.Length <= maxWidth)
                return wrappedText;

            StringBuilder sb = new StringBuilder();
            int currLineWidth = 0;

            string[] splitWords = wrappedText.Split(' ');
            sb.Append(splitWords[0]);
            currLineWidth += splitWords[0].Length;
            for (int i = 1; i < splitWords.Length; i++)
            {
                if (currLineWidth + splitWords[i].Length + 1 <= maxWidth)
                {
                    sb.Append(' ');
                    currLineWidth += splitWords[i].Length + 1;
                }
                else
                {
                    sb.Append('\n');
                    currLineWidth = splitWords[i].Length;
                }

                sb.Append(splitWords[i]);
            }

            return sb.ToString();
        }

        public override void Dispose()
        {
            base.Dispose();

            block.OnMarkForClose -= BlockDeleted;
        }

        void BlockDeleted(IMyEntity _)
        {
            Dispose();
        }
    }
}
