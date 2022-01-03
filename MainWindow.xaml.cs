//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//
// !!!IMPORTANT!!!
//
// Modified/changed for educational purposes by Maciej Madejczyk, Jakub Piechuła and Kacper Zwyrtek.
//
// We are using this software and Kinect overall to get bones and joints and send them to our server to steer an robot arm.
// We didn't write whole code by ourselves because it will be much slower to make. We are not making any profit by using this software and we are only learning it. 
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.SkeletonBasics
{

    ///Imports
    using System;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Windows;
    using System.Windows.Media;
    using Microsoft.Kinect;
    using Lego.Ev3.Core;
    using Lego.Ev3.Desktop;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Bool to check if left handed
        /// </summary>
        private bool leftHanded = false;
       
        /// <summary>
        /// Values sended to server
        /// </summary
        private NameValueCollection values = new NameValueCollection();
        
        /// <summary>
        /// Width of output drawing
        /// </summary>
        private const float RenderWidth = 640.0f;

        /// <summary>
        /// Height of our output drawing
        /// </summary>
        private const float RenderHeight = 480.0f;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 3;

        /// <summary>
        /// Thickness of body center ellipse
        /// </summary>
        private const double BodyCenterThickness = 10;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Brush used to draw skeleton center point
        /// </summary>
        private readonly Brush centerPointBrush = Brushes.Blue;

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 68, 192, 68));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;

        /// <summary>
        /// Pen used for drawing bones that are currently tracked
        /// </summary>
        private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);

        /// <summary>
        /// Pen used for drawing bones that are currently inferred
        /// </summary>        
        private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Drawing group for skeleton rendering output
        /// </summary>
        private DrawingGroup drawingGroup;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;

        /// <summary>
        /// Ev3 brick declaration
        /// </summary>
        private Brick brick;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private async void WindowLoaded(object sender, RoutedEventArgs e)
        {
            //Start bricks
            try
            {
                this.brick = new Brick(new BluetoothCommunication("COM4"));
                await this.brick.ConnectAsync();
                await this.brick.DirectCommand.PlayToneAsync(100, 600, 500);
                Trace.WriteLine("Brick started");
            }
            catch (IOException)
            {
                this.brick = null;
                Trace.WriteLine("No brick detected");
            }


            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();

            // Create an image source that we can use in our image control
            this.imageSource = new DrawingImage(this.drawingGroup);

            // Display the drawing using our image control
            Image.Source = this.imageSource;

            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the skeleton stream to receive skeleton frames
                this.sensor.SkeletonStream.Enable();

                // Add an event handler to be called whenever there is new color frame data
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                // Start the sensor!
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }

            if (null == this.sensor || null == this.brick)
            {
                this.statusBarText.Text = Properties.Resources.NoKinectReady;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }

            if (null != this.brick)
            {
                this.brick.Disconnect();
            }
        }

        /// <summary>
        /// Event handler for Kinect sensor's SkeletonFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);
                }
            }

            using (DrawingContext dc = this.drawingGroup.Open())
            {
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

                if (skeletons.Length != 0)
                {
                    foreach (Skeleton skel in skeletons)
                    {
                        if (skel.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            this.DrawBonesAndJoints(skel, dc);
                        }
                        else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
                        {
                            dc.DrawEllipse(
                            this.centerPointBrush,
                            null,
                            this.SkeletonPointToScreen(skel.Position),
                            BodyCenterThickness,
                            BodyCenterThickness);
                        }
                    }
                }

                // prevent drawing outside of our render area
                this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
            }
        }

        /// <summary>
        /// Draws a arms bones and joints and logs them
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
        {

            if (this.leftHanded)
            {
                // Render Left Arm
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
                this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
                this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

                Trace.WriteLine(
                    "Left shoulder: " + skeleton.Joints[JointType.ShoulderLeft].Position.X 
                    + " " + skeleton.Joints[JointType.ShoulderLeft].Position.Y 
                    + " " + skeleton.Joints[JointType.ShoulderLeft].Position.Z
                );
                Trace.WriteLine(
                    "Left elbow: " + skeleton.Joints[JointType.ElbowLeft].Position.X 
                    + " " + skeleton.Joints[JointType.ElbowLeft].Position.Y 
                    + " " + skeleton.Joints[JointType.ElbowLeft].Position.Z
                );
                Trace.WriteLine(
                    "Left wrist: " + skeleton.Joints[JointType.WristLeft].Position.X 
                    + " " + skeleton.Joints[JointType.WristLeft].Position.Y 
                    + " " + skeleton.Joints[JointType.WristLeft].Position.Z
                );
                Trace.WriteLine(
                    "Left hand: " + skeleton.Joints[JointType.HandLeft].Position.X 
                    + " " + skeleton.Joints[JointType.HandLeft].Position.Y 
                    + " " + skeleton.Joints[JointType.HandLeft].Position.Z
                );
                // Send data to brick
                moveRotors(false);
            }
            else
            {
                // Render Right Arm
                this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
                this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
                this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

                Trace.WriteLine(
                    "Right shoulder: " + skeleton.Joints[JointType.ShoulderRight].Position.X 
                    + " " + skeleton.Joints[JointType.ShoulderRight].Position.Y 
                    + " " + skeleton.Joints[JointType.ShoulderRight].Position.Z
                );
                Trace.WriteLine(
                    "Right elbow: " + skeleton.Joints[JointType.ElbowRight].Position.X 
                    + " " + skeleton.Joints[JointType.ElbowRight].Position.Y 
                    + " " + skeleton.Joints[JointType.ElbowRight].Position.Z
                );
                Trace.WriteLine(
                    "Right wrist: " + skeleton.Joints[JointType.WristRight].Position.X 
                    + " " + skeleton.Joints[JointType.WristRight].Position.Y 
                    + " " + skeleton.Joints[JointType.WristRight].Position.Z
                );
                Trace.WriteLine(
                    "Right hand: " + skeleton.Joints[JointType.HandRight].Position.X 
                    + " " + skeleton.Joints[JointType.HandRight].Position.Y 
                    + " " + skeleton.Joints[JointType.HandRight].Position.Z
                );

                // Send data to brick
                moveRotors(true);
            }

            
 
            // Render Joints
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;                    
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;                    
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                }
            }
        }

        /// <summary>
        /// moving robot 
        /// </summary>
        /// <param name="rotor">true means right hand, false left</param>
        private async void moveRotors(bool rotor)
        {
            if (rotor)
            {
                await this.brick.DirectCommand.StepMotorAtSpeedAsync(OutputPort.A, 50, 1000, false);
            }
            else
            {
                await this.brick.DirectCommand.StepMotorAtSpeedAsync(OutputPort.A, 50, 1000, false);
            }
        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            // We assume all drawn bones are inferred unless BOTH joints are tracked
            Pen drawPen = this.inferredBonePen;
            if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            {
                drawPen = this.trackedBonePen;
            }

            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
        }

        /// <summary>
        /// Handles the checking or unchecking of the seated mode combo box
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckBoxSeatedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkBoxSeatedMode.IsChecked.GetValueOrDefault())
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                }
                else
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                }
            }
        }

        /// <summary>
        /// Handles changing your hand to left one
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckLeftHandedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkLeftHandedMode.IsChecked.GetValueOrDefault())
                {
                    this.leftHanded = true;
                }
                else
                {
                    this.leftHanded = false;
                }
            }
        }
    }
}