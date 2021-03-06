﻿//------------------------------------------------------------------------------
// <copyright file="FallingThings.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

// This module contains code to do display falling shapes, and do
// hit testing against a set of segments provided by the Kinect NUI, and
// have shapes react accordingly.

namespace ShapeGame
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Shapes;
    using Microsoft.Kinect;
    using ShapeGame.Utils;

    // FallingThings is the main class to draw and maintain positions of falling shapes.  It also does hit testing
    // and appropriate bouncing.
    public class FallingThings
    {
        private const double BaseGravity = 0.017;
        private const double BaseAirFriction = 0.994;

        private readonly List<Spider> things = new List<Spider>();
        private readonly Random rnd = new Random();
        private readonly int maxThings;
        private readonly int intraFrames = 1;
        private readonly Dictionary<int, int> scores = new Dictionary<int, int>();
        private const double DissolveTime = 0.4;
        private Rect sceneRect;
        private double targetFrameRate = 60;
        private double dropRate = 2.0;
        private double shapeSize = 1.0;
        private double baseShapeSize = 20;
        private GameMode gameMode = GameMode.Off;
        private double gravity = BaseGravity;
        private double gravityFactor = 1.0;
        private double airFriction = BaseAirFriction;
        private int frameCount;
        private bool doRandomColors = true;
        private double expandingRate = 1.0;
        private System.Windows.Media.Color baseColor = System.Windows.Media.Color.FromRgb(0, 0, 0);
        private PolyType polyTypes = PolyType.All;
        private DateTime gameStartTime;

        public FallingThings(int maxThings, double framerate, int intraFrames)
        {
            this.maxThings = maxThings;
            this.intraFrames = intraFrames;
            this.targetFrameRate = framerate * intraFrames;
            this.SetGravity(this.gravityFactor);
            this.sceneRect.X = this.sceneRect.Y = 0;
            this.sceneRect.Width = this.sceneRect.Height = 100;
            this.shapeSize = this.sceneRect.Height * this.baseShapeSize / 1000.0;
            this.expandingRate = Math.Exp(Math.Log(6.0) / (this.targetFrameRate * DissolveTime));
        }

        public static Label MakeSimpleLabel(string text, Rect bounds, System.Windows.Media.Brush brush)
        {
            Label label = new Label { Content = text };
            if (bounds.Width != 0)
            {
                label.SetValue(Canvas.LeftProperty, bounds.Left);
                label.SetValue(Canvas.TopProperty, bounds.Top);
                label.Width = bounds.Width;
                label.Height = bounds.Height;
            }

            label.Foreground = brush;
            label.FontFamily = new System.Windows.Media.FontFamily("Arial");
            label.FontWeight = FontWeight.FromOpenTypeWeight(600);
            label.FontStyle = FontStyles.Normal;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            return label;
        }

        public void SetFramerate(double actualFramerate)
        {
            this.targetFrameRate = actualFramerate * this.intraFrames;
            this.expandingRate = Math.Exp(Math.Log(6.0) / (this.targetFrameRate * DissolveTime));
            if (this.gravityFactor != 0)
            {
                this.SetGravity(this.gravityFactor);
            }
        }

        public void SetBoundaries(Rect r)
        {
            this.sceneRect = r;
            this.shapeSize = r.Height * this.baseShapeSize / 1000.0;
        }

        public void SetDropRate(double f)
        {
            this.dropRate = f;
        }

        public void SetSize(double f)
        {
            this.baseShapeSize = f;
            this.shapeSize = this.sceneRect.Height * this.baseShapeSize / 1000.0;
        }

        public void SetShapesColor(System.Windows.Media.Color color, bool doRandom)
        {
            this.doRandomColors = doRandom;
            this.baseColor = color;
        }

        public void Reset()
        {
            for (int i = 0; i < this.things.Count; i++)
            {
                var thing = this.things[i];
                if ((thing.State == ThingState.Bouncing) || (thing.State == ThingState.Falling))
                {
                    thing.State = ThingState.Dissolving;
                    thing.Dissolve = 0;
                    this.things[i] = thing;
                }
            }

            this.gameStartTime = DateTime.Now;
            this.scores.Clear();
        }

        public void SetGameMode(GameMode mode)
        {
            this.gameMode = mode;
            this.gameStartTime = DateTime.Now;
            this.scores.Clear();
        }

        public void SetGravity(double f)
        {
            this.gravityFactor = f;
            this.gravity = f * BaseGravity / this.targetFrameRate / Math.Sqrt(this.targetFrameRate) / Math.Sqrt(this.intraFrames);
            this.airFriction = f == 0 ? 0.997 : Math.Exp(Math.Log(1.0 - ((1.0 - BaseAirFriction) / f)) / this.intraFrames);

            if (f == 0)
            {
                // Stop all movement as well!
                for (int i = 0; i < this.things.Count; i++)
                {
                    var thing = this.things[i];
                    thing.XVelocity = thing.YVelocity = 0;
                    this.things[i] = thing;
                }
            }
        }

        public void SetPolies(PolyType polies)
        {
            this.polyTypes = polies;
        }

        public HitType LookForHits(Dictionary<Bone, BoneData> segments, int playerId, Player player)
        {
            DateTime cur = DateTime.Now;
            HitType allHits = HitType.None;

            // Zero out score if necessary
            if (!this.scores.ContainsKey(playerId))
            {
                this.scores.Add(playerId, 0);
            }

            foreach (var pair in segments)
            {
                for (int i = 0; i < this.things.Count; i++)
                {
                    HitType hit = HitType.None;
                    var thing = this.things[i];
                    switch (thing.State)
                    {
                        case ThingState.Bouncing:
                        case ThingState.Falling:
                        case ThingState.Attached:
                            var hitCenter = new System.Windows.Point(0, 0);
                            double lineHitLocation = 0;
                            Segment seg = pair.Value.GetEstimatedSegment(cur);
                            if (thing.Hit(seg, ref hitCenter, ref lineHitLocation))
                            {
                                double fMs = 1000;
                                if (thing.TimeLastHit != DateTime.MinValue)
                                {
                                    fMs = cur.Subtract(thing.TimeLastHit).TotalMilliseconds;
                                    thing.AvgTimeBetweenHits = (thing.AvgTimeBetweenHits * 0.8) + (0.2 * fMs);
                                }

                                thing.TimeLastHit = cur;

                                // Bounce off head and hands
                                if (seg.IsCircle())
                                {
                                    // Bounce off of hand/head/foot
                                    thing.BounceOff(
                                        hitCenter.X,
                                        hitCenter.Y,
                                        seg.Radius,
                                        pair.Value.XVelocity / this.targetFrameRate,
                                        pair.Value.YVelocity / this.targetFrameRate);

                                    if (fMs > 100.0)
                                    {
                                        hit |= HitType.Hand;
                                    }
                                    thing.State = ThingState.Bouncing;
                                }
                                else
                                {
                                    if (thing.State != ThingState.Attached)
                                    {
                                        thing.State = ThingState.Attached;
                                        thing.attachedPlayer = player;
                                        thing.attachedTo = pair.Key;
                                        thing.attachedAt = lineHitLocation;
                                    }
                                }

                                if (thing.State == ThingState.Attached)
                                {
                                    break;
                                }

                                if (this.gameMode == GameMode.TwoPlayer)
                                {
                                    if (thing.State == ThingState.Falling)
                                    {
                                        thing.State = ThingState.Bouncing;
                                        thing.TouchedBy = playerId;
                                        thing.Hotness = 1;
                                        thing.FlashCount = 0;
                                    }
                                    else if (thing.State == ThingState.Bouncing)
                                    {
                                        if (thing.TouchedBy != playerId)
                                        {
                                            if (seg.IsCircle())
                                            {
                                                thing.TouchedBy = playerId;
                                                thing.Hotness = Math.Min(thing.Hotness + 1, 4);
                                            }
                                            else
                                            {
                                                hit |= HitType.Popped;
                                                this.AddToScore(thing.TouchedBy, 5 << (thing.Hotness - 1), thing.Center);
                                            }
                                        }
                                    }
                                }
                                else if (this.gameMode == GameMode.Solo)
                                {
                                    if (seg.IsCircle())
                                    {
                                        if (thing.State == ThingState.Falling)
                                        {
                                            thing.State = ThingState.Bouncing;
                                            thing.TouchedBy = playerId;
                                            thing.Hotness = 1;
                                            thing.FlashCount = 0;
                                        }
                                        else if ((thing.State == ThingState.Bouncing) && (fMs > 100.0))
                                        {
                                            hit |= HitType.Popped;
                                            int points = (pair.Key.Joint1 == JointType.FootLeft
                                                          || pair.Key.Joint1 == JointType.FootRight)
                                                             ? 10
                                                             : 5;
                                            this.AddToScore(
                                                thing.TouchedBy,
                                                points,
                                                thing.Center);
                                            thing.TouchedBy = playerId;
                                        }
                                    }
                                }

                                this.things[i] = thing;

                                if (thing.AvgTimeBetweenHits < 8)
                                {
                                    hit |= HitType.Popped | HitType.Squeezed;
                                    if (this.gameMode != GameMode.Off)
                                    {
                                        this.AddToScore(playerId, 1, thing.Center);
                                    }
                                }
                            }
                            break;
                    }

                    if ((hit & HitType.Popped) != 0)
                    {
                        thing.State = ThingState.Dissolving;
                        thing.Dissolve = 0;
                        thing.XVelocity = thing.YVelocity = 0;
                        thing.SpinRate = (thing.SpinRate * 6) + 0.2;
                        this.things[i] = thing;
                    }

                    allHits |= hit;
                }
            }

            return allHits;
        }

        public void AdvanceFrame()
        {
            // Move all things by one step, accounting for gravity
            for (int thingIndex = 0; thingIndex < this.things.Count; thingIndex++)
            {
                var thing = this.things[thingIndex];
                thing.Center.Offset(thing.XVelocity, thing.YVelocity);
                thing.YVelocity += this.gravity * this.sceneRect.Height;
                thing.YVelocity *= this.airFriction;
                thing.XVelocity *= this.airFriction;
                thing.Theta += thing.SpinRate;

                if (thing.State == ThingState.Attached)
                {
                    Player attch = thing.attachedPlayer;
                    Segment seg = attch.Segments[thing.attachedTo].Segment;

                    thing.Center.X = seg.X1 + (seg.X2 - seg.X1) * thing.attachedAt;
                    thing.Center.Y = seg.Y1 + (seg.Y2 - seg.Y1) * thing.attachedAt;
                }
                else
                {
                    // bounce off walls
                    if ((thing.Center.X - thing.Size < 0) || (thing.Center.X + thing.Size > this.sceneRect.Width))
                    {
                        thing.XVelocity = -thing.XVelocity;
                        thing.Center.X += thing.XVelocity;
                    }

                    // Then get rid of one if any that fall off the bottom
                    if (thing.Center.Y - thing.Size > this.sceneRect.Bottom)
                    {
                        thing.State = ThingState.Remove;
                    }

                    // Get rid of after dissolving.
                    if (thing.State == ThingState.Dissolving)
                    {
                        thing.Dissolve += 1 / (this.targetFrameRate * DissolveTime);
                        thing.Size *= this.expandingRate;
                        if (thing.Dissolve >= 1.0)
                        {
                            thing.State = ThingState.Remove;
                        }
                    }

                }

                this.things[thingIndex] = thing;
            }

            // Then remove any that should go away now
            for (int i = 0; i < this.things.Count; i++)
            {
                var thing = this.things[i];
                if (thing.State == ThingState.Remove)
                {
                    this.things.Remove(thing);
                    i--;
                }
            }

            // Create any new things to drop based on dropRate
            if ((this.things.Count < this.maxThings) && (this.rnd.NextDouble() < this.dropRate / this.targetFrameRate) && (this.polyTypes != PolyType.None))
            {
                PolyType[] alltypes = 
                {
                    PolyType.Triangle, PolyType.Square, PolyType.Star, PolyType.Pentagon,
                    PolyType.Hex, PolyType.Star7, PolyType.Circle, PolyType.Bubble
                };
                byte r;
                byte g;
                byte b;

                if (this.doRandomColors)
                {
                    r = (byte)(this.rnd.Next(215) + 40);
                    g = (byte)(this.rnd.Next(215) + 40);
                    b = (byte)(this.rnd.Next(215) + 40);
                }
                else
                {
                    r = (byte)Math.Min(255.0, this.baseColor.R * (0.7 + (this.rnd.NextDouble() * 0.7)));
                    g = (byte)Math.Min(255.0, this.baseColor.G * (0.7 + (this.rnd.NextDouble() * 0.7)));
                    b = (byte)Math.Min(255.0, this.baseColor.B * (0.7 + (this.rnd.NextDouble() * 0.7)));
                }

                PolyType tryType;
                do
                {
                    tryType = alltypes[this.rnd.Next(alltypes.Length)];
                }
                while ((this.polyTypes & tryType) == 0);

                this.DropNewThing(tryType, this.shapeSize, System.Windows.Media.Color.FromRgb(r, g, b));
            }
        }

        public void DrawFrame(UIElementCollection children)
        {
            this.frameCount++;

            // Draw all shapes in the scene
            for (int i = 0; i < this.things.Count; i++)
            {
                var thing = this.things[i];
                thing.Draw(children);
            }

            // Show scores
            if (this.scores.Count != 0)
            {
                int i = 0;
                foreach (var score in this.scores)
                {
                    Label label = MakeSimpleLabel(
                        score.Value.ToString(CultureInfo.InvariantCulture),
                        new Rect(
                            (0.02 + (i * 0.6)) * this.sceneRect.Width,
                            0.01 * this.sceneRect.Height,
                            0.4 * this.sceneRect.Width,
                            0.3 * this.sceneRect.Height),
                            new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)));
                    label.FontSize = Math.Max(1, Math.Min(this.sceneRect.Width / 12, this.sceneRect.Height / 12));
                    children.Add(label);
                    i++;
                }
            }

            // Show game timer
            if (this.gameMode != GameMode.Off)
            {
                TimeSpan span = DateTime.Now.Subtract(this.gameStartTime);
                string text = span.Minutes.ToString(CultureInfo.InvariantCulture) + ":" + span.Seconds.ToString("00");

                Label timeText = MakeSimpleLabel(
                    text,
                    new Rect(
                        0.1 * this.sceneRect.Width, 0.25 * this.sceneRect.Height, 0.89 * this.sceneRect.Width, 0.72 * this.sceneRect.Height),
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 255, 255)));
                timeText.FontSize = Math.Max(1, this.sceneRect.Height / 16);
                timeText.HorizontalContentAlignment = HorizontalAlignment.Right;
                timeText.VerticalContentAlignment = VerticalAlignment.Bottom;
                children.Add(timeText);
            }
        }

        private void AddToScore(int player, int points, System.Windows.Point center)
        {
            if (this.scores.ContainsKey(player))
            {
                this.scores[player] = this.scores[player] + points;
            }
            else
            {
                this.scores.Add(player, points);
            }

            FlyingText.NewFlyingText(this.sceneRect.Width / 300, center, "+" + points);
        }

        private void DropNewThing(PolyType newShape, double newSize, System.Windows.Media.Color newColor)
        {
            // Only drop within the center "square" area 
            double dropWidth = this.sceneRect.Bottom - this.sceneRect.Top;
            if (dropWidth > this.sceneRect.Right - this.sceneRect.Left)
            {
                dropWidth = this.sceneRect.Right - this.sceneRect.Left;
            }

            var newThing = new Spider
            {
                Size = newSize,
                YVelocity = ((0.5 * this.rnd.NextDouble()) - 0.25) / this.targetFrameRate,
                XVelocity = 0,
                Shape = newShape,
                Center = new System.Windows.Point((this.rnd.NextDouble() * dropWidth) + ((this.sceneRect.Left + this.sceneRect.Right - dropWidth) / 2), this.sceneRect.Top - newSize),
                SpinRate = ((this.rnd.NextDouble() * 12.0) - 6.0) * 2.0 * Math.PI / this.targetFrameRate / 4.0,
                Theta = 0,
                TimeLastHit = DateTime.MinValue,
                AvgTimeBetweenHits = 100,
                Color = newColor,
                Brush = null,
                Brush2 = null,
                BrushPulse = null,
                Dissolve = 0,
                State = ThingState.Falling,
                TouchedBy = 0,
                Hotness = 0,
                FlashCount = 0
            };

            this.things.Add(newThing);
        }

        internal struct PolyDef
        {
            public int Sides;
            public int Skip;
        }

        // The Thing struct represents a single object that is flying through the air, and
        // all of its properties.
        private class Thing
        {
            public System.Windows.Point Center;
            public double Size;
            public double Theta;             // sekarang lagi dirotasi sejauh brp
            public double SpinRate;          // perubahan theta per frame
            public double YVelocity;
            public double XVelocity;
            public PolyType Shape;
            public System.Windows.Media.Color Color;
            public System.Windows.Media.Brush Brush;
            public System.Windows.Media.Brush Brush2;
            public System.Windows.Media.Brush BrushPulse;
            public double Dissolve;
            public ThingState State;
            public DateTime TimeLastHit;
            public double AvgTimeBetweenHits;
            public int TouchedBy;               // Last player to touch this thing
            public int Hotness;                 // Score level
            public int FlashCount;

            private static readonly Dictionary<PolyType, PolyDef> polyDefs = new Dictionary<PolyType, PolyDef>
                {
                    { PolyType.Triangle, new PolyDef { Sides = 3, Skip = 1 } },
                    { PolyType.Star, new PolyDef { Sides = 5, Skip = 2 } },
                    { PolyType.Pentagon, new PolyDef { Sides = 5, Skip = 1 } },
                    { PolyType.Square, new PolyDef { Sides = 4, Skip = 1 } },
                    { PolyType.Hex, new PolyDef { Sides = 6, Skip = 1 } },
                    { PolyType.Star7, new PolyDef { Sides = 7, Skip = 3 } },
                    { PolyType.Circle, new PolyDef { Sides = 1, Skip = 1 } }
                };

            // Change our velocity based on the object's velocity, our velocity, and where we hit.
            public void BounceOff(double x1, double y1, double otherSize, double fXv, double fYv)
            {
                double x0 = this.Center.X;
                double y0 = this.Center.Y;
                double xv0 = this.XVelocity - fXv;
                double yv0 = this.YVelocity - fYv;
                double dist = otherSize + this.Size;
                double dx = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
                double xdif = x1 - x0;
                double ydif = y1 - y0;
                double newvx1 = 0;
                double newvy1 = 0;

                x0 = x1 - (xdif / dx * dist);
                y0 = y1 - (ydif / dx * dist);
                xdif = x1 - x0;
                ydif = y1 - y0;

                double bsq = dist * dist;
                double b = dist;
                double asq = (xv0 * xv0) + (yv0 * yv0);
                double a = Math.Sqrt(asq);
                if (a > 0.000001)
                {
                    // if moving much at all...
                    double cx = x0 + xv0;
                    double cy = y0 + yv0;
                    double csq = ((x1 - cx) * (x1 - cx)) + ((y1 - cy) * (y1 - cy));
                    double tt = asq + bsq - csq;
                    double bb = 2 * a * b;
                    double power = a * (tt / bb);
                    newvx1 -= 2 * (xdif / dist * power);
                    newvy1 -= 2 * (ydif / dist * power);
                }

                this.XVelocity += newvx1;
                this.YVelocity += newvy1;
                this.Center.X = x0;
                this.Center.Y = y0;
            }

            public void Draw(UIElementCollection children)
            {
                if (this.Brush == null)
                {
                    this.Brush = new SolidColorBrush(this.Color);
                    double factor = 0.4 + (((double)this.Color.R + this.Color.G + this.Color.B) / 1600);
                    this.Brush2 =
                        new SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                (byte)(255 - ((255 - this.Color.R) * factor)),
                                (byte)(255 - ((255 - this.Color.G) * factor)),
                                (byte)(255 - ((255 - this.Color.B) * factor))));
                    this.BrushPulse = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
                }

                if (this.State == ThingState.Bouncing)
                {
                    // Pulsate edges
                    double alpha = Math.Cos((0.15 * (this.FlashCount++) * this.Hotness) * 0.5) + 0.5;

                    children.Add(
                        this.MakeSimpleShape(
                            this.BrushPulse,
                            this.Size * 0.1,
                            alpha));
                }
                else
                {
                    if (this.State == ThingState.Dissolving)
                    {
                        this.Brush.Opacity = 1.0 - (this.Dissolve * this.Dissolve);
                    }

                    children.Add(
                        this.MakeSimpleShape(
                            (this.State == ThingState.Dissolving) ? null : this.Brush2,
                            1,
                            1));
                }
            }

            private Shape MakeSimpleShape(
                System.Windows.Media.Brush brushStroke,
                double strokeThickness,
                double opacity)
            {
                int numSides = Thing.polyDefs[this.Shape].Sides;
                int skip = Thing.polyDefs[this.Shape].Skip;
                double size = this.Size;
                double spin = this.Theta;
                System.Windows.Point center = this.Center;
                System.Windows.Media.Brush brush = this.Brush;

                if (numSides <= 1)
                {
                    var circle = new Ellipse { Width = size * 2, Height = size * 2, Stroke = brushStroke };
                    if (circle.Stroke != null)
                    {
                        circle.Stroke.Opacity = opacity;
                    }

                    circle.StrokeThickness = strokeThickness * ((numSides == 1) ? 1 : 2);
                    circle.Fill = (numSides == 1) ? brush : null;
                    circle.SetValue(Canvas.LeftProperty, center.X - size);
                    circle.SetValue(Canvas.TopProperty, center.Y - size);
                    return circle;
                }

                var points = new PointCollection(numSides + 2);
                double theta = spin;
                for (int i = 0; i <= numSides + 1; ++i)
                {
                    points.Add(new System.Windows.Point((Math.Cos(theta) * size) + center.X, (Math.Sin(theta) * size) + center.Y));
                    theta = theta + (2.0 * Math.PI * skip / numSides);
                }

                var polyline = new Polyline { Points = points, Stroke = brushStroke };
                if (polyline.Stroke != null)
                {
                    polyline.Stroke.Opacity = opacity;
                }

                polyline.Fill = brush;
                polyline.FillRule = FillRule.Nonzero;
                polyline.StrokeThickness = strokeThickness;
                return polyline;
            }
        }

    }
}
