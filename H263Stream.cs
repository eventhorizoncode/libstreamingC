/*
 * Copyright (C) 2011-2015 GUIGUI Simon, fyhertz@gmail.com
 *
 * This file is part of libstreaming (https://github.com/fyhertz/libstreaming)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/**
 * A class for streaming H.263 from the camera of an android device using RTP.
 * You should use a {@link Session} instantiated with {@link SessionBuilder} instead of using this class directly.
 * Call {@link #setDestinationAddress(InetAddress)}, {@link #setDestinationPorts(int)} and {@link #setVideoQuality(VideoQuality)}
 * to configure the stream. You can then call {@link #start()} to start the RTP stream.
 * Call {@link #stop()} to stop the stream.
 */
 using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Preferences;
using Android.Util;
using Java.IO;
using Java.Lang;
using Java.Nio;
using System;
using System.Collections;
using System.Collections.Generic;

using static Android.Media.MediaCodec;

namespace Net.Majorkernelpanic.Streaming
{
    public class H263Stream : VideoStream
    {


        private void constructStream(int cameraId)
        {
            mCameraImageFormat = (int)ImageFormatType.Nv21;
            mVideoEncoder = (int)VideoEncoder.H263;
            mPacketizer = new H263Packetizer();
        }
        /**
	     * Constructs the H.263 stream.
	     * Uses CAMERA_FACING_BACK by default.
	     * @throws IOException
	     */
        public H263Stream() {
            constructStream((int)Android.Hardware.CameraFacing.Front);
	    }	

	    /**
	     * Constructs the H.263 stream.
	     * @param cameraId Can be either CameraInfo.CAMERA_FACING_BACK or CameraInfo.CAMERA_FACING_FRONT 
	     * @throws IOException
	     */	
	    public H263Stream(int cameraId) :base(cameraId) {
            constructStream(cameraId);
        }

	    /**
	     * Starts the stream.
	     */
	    public void start() {
            lock(this)
            { 
		        if (!mStreaming) {
			        configure();
			        base.start();
		        }
            }
        }
	
	    public void configure() {
            lock(this)
            { 
		        base.configure();
		        mMode = MODE_MEDIARECORDER_API;
		        mQuality = mRequestedQuality.clone();
            }
        }
	
	    /**
	     * Returns a description of the stream using SDP. It can then be included in an SDP file.
	     */
	    public override System.String getSessionDescription() {
		    return "m=video "+ getDestinationPorts()[0].ToString()+" RTP/AVP 96\r\n" +
				    "a=rtpmap:96 H263-1998/90000\r\n";
	    }

    }

}