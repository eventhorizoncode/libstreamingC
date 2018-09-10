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

//package net.majorkernelpanic.streaming.audio;

/*
import java.io.IOException;
import java.lang.reflect.Field;
import net.majorkernelpanic.streaming.SessionBuilder;
import net.majorkernelpanic.streaming.rtp.AMRNBPacketizer;
import android.media.MediaRecorder;
import android.service.textservice.SpellCheckerService.Session;
*/
/**
 * A class for streaming AAC from the camera of an android device using RTP.
 * You should use a {@link Session} instantiated with {@link SessionBuilder} instead of using this class directly.
 * Call {@link #setDestinationAddress(InetAddress)}, {@link #setDestinationPorts(int)} and {@link #setAudioQuality(AudioQuality)}
 * to configure the stream. You can then call {@link #start()} to start the RTP stream.
 * Call {@link #stop()} to stop the stream.
 */
 
using Android.Util;
using Java.IO;
using Java.Lang;
using Java.Net;
using Java.Util;
using System;
using Android.Service.Textservice;
using Android.Media;

namespace Net.Majorkernelpanic.Streaming
{
    public class AMRNBStream : AudioStream
    {

        public AMRNBStream()
        {


            mPacketizer = new AMRNBPacketizer();

            setAudioSource((int)Android.Media.AudioSource.Camcorder);

            // RAW_AMR was deprecated in API level 16.
            Type mType = typeof(MediaRecorder.OutputFormat);

            if (mType.GetField("RAW_AMR") == null)
            {
                //CHECK CODE
                setOutputFormat(0);
            }
            else
            {
                setOutputFormat((int)Android.Media.AudioEncoder.AmrNb);
            }

            setAudioEncoder((int)Android.Media.AudioEncoder.AmrNb);

        }

        /**
         * Starts the stream.
         */
        public new void start()
        {
            lock (this)
            {
                if (!mStreaming)
                {
                    configure();
                    base.start();
                }
            }
        }

        public new void configure()
        {
            base.configure();
            mMode = MODE_MEDIARECORDER_API;
            mQuality = mRequestedQuality.clone();
        }

        /**
         * Returns a description of the stream using SDP. It can then be included in an SDP file.
         */
        public override System.String getSessionDescription()
        {
            return "m=audio " + Java.Lang.String.ValueOf(getDestinationPorts()[0]) + " RTP/AVP 96\r\n" +
                    "a=rtpmap:96 AMR/8000\r\n" +
                    "a=fmtp:96 octet-align=1;\r\n";
        }


        protected override void encodeWithMediaCodec()
        {

            base.encodeWithMediaRecorder();
        }

    }
}
