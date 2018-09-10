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





using Android.Media;
using Android.OS;
using Android.Util;
using Android.Preferences;
using Java.Lang;
using System;
using Android.Content;
using Java.IO;
/**
* A class for streaming AAC from the camera of an android device using RTP.
* You should use a {@link Session} instantiated with {@link SessionBuilder} instead of using this class directly.
* Call {@link #setDestinationAddress(InetAddress)}, {@link #setDestinationPorts(int)} and {@link #setAudioQuality(AudioQuality)}
* to configure the stream. You can then call {@link #start()} to start the RTP stream.
* Call {@link #stop()} to stop the stream.
*/
namespace Net.Majorkernelpanic.Streaming
{
    public class AACStream : AudioStream, IRunnable
    {

        public const System.String TAG = "AACStream";

        /** MPEG-4 Audio Object Types supported by ADTS. **/
        private System.String[] AUDIO_OBJECT_TYPES = {
        "NULL",							  // 0
		"AAC Main",						  // 1
		"AAC LC (Low Complexity)",		  // 2
		"AAC SSR (Scalable Sample Rate)", // 3
		"AAC LTP (Long Term Prediction)"  // 4	
	};

        /** There are 13 supported frequencies by ADTS. **/
        public static int[] AUDIO_SAMPLING_RATES = {
        96000, // 0
		88200, // 1
		64000, // 2
		48000, // 3
		44100, // 4
		32000, // 5
		24000, // 6
		22050, // 7
		16000, // 8
		12000, // 9
		11025, // 10
		8000,  // 11
		7350,  // 12
		-1,   // 13
		-1,   // 14
		-1,   // 15
	};

        private System.String mSessionDescription = null;
        private int mProfile, mSamplingRateIndex, mChannel, mConfig;
        private ISharedPreferences mSettings = null;
        private AudioRecord mAudioRecord = null;
        private Thread mThread = null;

        public IntPtr Handle => throw new NotImplementedException();

        public AACStream()
        {


            if (!AACStreamingSupported())
            {
                Log.Error(TAG, "AAC not supported on this phone");
                throw new RuntimeException("AAC not supported by this phone !");
            }
            else
            {
                Log.Debug(TAG, "AAC supported on this phone");
            }

        }

        private static bool AACStreamingSupported()
        {
            if ((int)Build.VERSION.SdkInt < 14) return false;
            try
            {
                Type mType = typeof(MediaRecorder.OutputFormat);

                if (mType.GetField("AAC_ADTS") == null)
                {
                    return false;
                }

                return true;
            }
            catch (Java.Lang.Exception e)
            {
                return false;
            }
        }

        /**
         * Some data (the actual sampling rate used by the phone and the AAC profile) needs to be stored once {@link #getSessionDescription()} is called.
         * @param prefs The SharedPreferences that will be used to store the sampling rate 
         */
        public void setPreferences(ISharedPreferences prefs)
        {
            mSettings = prefs;
        }


        public void start()
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

        public void configure()
        {
            lock (this)
            {

                base.configure();
                mQuality = mRequestedQuality.clone();

                // Checks if the user has supplied an exotic sampling rate
                int i = 0;
                for (; i < AUDIO_SAMPLING_RATES.Length; i++)
                {
                    if (AUDIO_SAMPLING_RATES[i] == mQuality.samplingRate)
                    {
                        mSamplingRateIndex = i;
                        break;
                    }
                }
                // If he did, we force a reasonable one: 16 kHz
                if (i > 12) mQuality.samplingRate = 16000;

                if (mMode != mRequestedMode || mPacketizer == null)
                {
                    mMode = mRequestedMode;
                    if (mMode == MODE_MEDIARECORDER_API)
                    {
                        mPacketizer = new AACADTSPacketizer();
                    }
                    else
                    {
                        mPacketizer = new AACLATMPacketizer();
                    }
                    mPacketizer.setDestination(mDestination, mRtpPort, mRtcpPort);
                    mPacketizer.getRtpSocket().setOutputStream(mOutputStream, mChannelIdentifier);
                }

                if (mMode == MODE_MEDIARECORDER_API)
                {

                    testADTS();

                    // All the MIME types parameters used here are described in RFC 3640
                    // SizeLength: 13 bits will be enough because ADTS uses 13 bits for frame length
                    // config: contains the object type + the sampling rate + the channel number

                    // TODO: streamType always 5 ? profile-level-id always 15 ?

                    mSessionDescription = "m=audio " + System.String.valueOf(getDestinationPorts()[0]) + " RTP/AVP 96\r\n" +
                            "a=rtpmap:96 mpeg4-generic/" + mQuality.samplingRate + "\r\n" +
                            "a=fmtp:96 streamtype=5; profile-level-id=15; mode=AAC-hbr; config=" + Integer.ToHexString(mConfig) + "; SizeLength=13; IndexLength=3; IndexDeltaLength=3;\r\n";

                }
                else
                {

                    mProfile = 2; // AAC LC
                    mChannel = 1;
                    mConfig = (mProfile & 0x1F) << 11 | (mSamplingRateIndex & 0x0F) << 7 | (mChannel & 0x0F) << 3;

                    mSessionDescription = "m=audio " + System.String.valueOf(getDestinationPorts()[0]) + " RTP/AVP 96\r\n" +
                            "a=rtpmap:96 mpeg4-generic/" + mQuality.samplingRate + "\r\n" +
                            "a=fmtp:96 streamtype=5; profile-level-id=15; mode=AAC-hbr; config=" + Integer.ToHexString(mConfig) + "; SizeLength=13; IndexLength=3; IndexDeltaLength=3;\r\n";

                }
            }


        }

        protected override void encodeWithMediaRecorder()
        {
            testADTS();
            ((AACADTSPacketizer)mPacketizer).setSamplingRate(mQuality.samplingRate);
            base.encodeWithMediaRecorder();
        }

        //@Override
        //@SuppressLint({ "InlinedApi", "NewApi" })
        protected override void encodeWithMediaCodec()
        {

            int bufferSize = AudioRecord.GetMinBufferSize(mQuality.samplingRate, ChannelIn.Mono, Encoding.Pcm16bit) * 2;

            ((AACLATMPacketizer)mPacketizer).setSamplingRate(mQuality.samplingRate);

            mAudioRecord = new AudioRecord(Android.Media.AudioSource.Mic, mQuality.samplingRate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, bufferSize);
            mMediaCodec = MediaCodec.CreateEncoderByType("audio/mp4a-latm");
            MediaFormat format = new MediaFormat();
            format.SetString(MediaFormat.KeyMime, "audio/mp4a-latm");
            format.SetInteger(MediaFormat.KeyBitRate, mQuality.bitRate);
            format.SetInteger(MediaFormat.KeyChannelCount, 1);
            format.SetInteger(MediaFormat.KeySampleRate, mQuality.samplingRate);
            format.SetInteger(MediaFormat.KeyAacProfile, (int)MediaCodecInfo.CodecProfileLevel.AACObjectLC);
            format.SetInteger(MediaFormat.KeyMaxInputSize, bufferSize);
            mMediaCodec.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            mAudioRecord.StartRecording();
            mMediaCodec.Start();

            MediaCodecInputStream inputStream = new MediaCodecInputStream(mMediaCodec);
            Java.Nio.ByteBuffer[] inputBuffers = mMediaCodec.GetInputBuffers();

            mThread = new Thread(this);


            mThread.Start();

            // The packetizer encapsulates this stream in an RTP stream and send it over the network
            mPacketizer.setInputStream(inputStream);
            mPacketizer.start();

            mStreaming = true;

        }

        public void Run()
        {
            int len = 0, bufferIndex = 0;
            try
            {
                Java.Nio.ByteBuffer[] inputBuffers = mMediaCodec.GetInputBuffers();
                int bufferSize = AudioRecord.GetMinBufferSize(mQuality.samplingRate, ChannelIn.Mono, Encoding.Pcm16bit) * 2;
                while (!Thread.Interrupted())
                {
                    bufferIndex = mMediaCodec.DequeueInputBuffer(10000);
                    if (bufferIndex >= 0)
                    {
                        inputBuffers[bufferIndex].Clear();
                        len = mAudioRecord.Read(inputBuffers[bufferIndex], bufferSize);
                        if ((len == (int)RecordStatus.ErrorInvalidOperation) || (len == (int)RecordStatus.ErrorBadValue))
                        {
                            Log.Error(TAG, "An error occured with the AudioRecord API !");
                        }
                        else
                        {
                            //Log.v(TAG,"Pushing raw audio to the decoder: len="+len+" bs: "+inputBuffers[bufferIndex].capacity());
                            mMediaCodec.QueueInputBuffer(bufferIndex, 0, len, Java.Lang.JavaSystem.NanoTime() / 1000, 0);
                        }
                    }
                }
            }
            catch (RuntimeException e)
            {
                e.PrintStackTrace();
            }
        }

        public void Dispose()
        {

        }

        /** Stops the stream. */
        public void stop()
        {
            if (mStreaming)
            {
                if (mMode == MODE_MEDIACODEC_API)
                {
                    Log.d(TAG, "Interrupting threads...");
                    mThread.Interrupt();
                    mAudioRecord.Stop();
                    mAudioRecord.Release();
                    mAudioRecord = null;
                }
                base.stop();
            }
        }

        /**
         * Returns a description of the stream using SDP. It can then be included in an SDP file.
         * Will fail if called when streaming.
         */
        public System.String getSessionDescription()
        {
            if (mSessionDescription == null) throw new IllegalStateException("You need to call configure() first !");
            return mSessionDescription;
        }

        /** 
         * Records a short sample of AAC ADTS from the microphone to find out what the sampling rate really is
         * On some phone indeed, no error will be reported if the sampling rate used differs from the 
         * one selected with setAudioSamplingRate 
         * @throws IOException 
         * @throws IllegalStateException
         */

        private void testADTS()
        {

            setAudioEncoder((int)Android.Media.AudioEncoder.Aac);
            try
            {
                Type mType = typeof(MediaRecorder.OutputFormat);

                if (mType.GetField("AAC_ADTS") == null)
                {
                    return;
                }

                setOutputFormat((int)Android.Media.AudioEncoder.Aac);
            }
            catch (System.Exception ignore)
            {
                setOutputFormat(6);
            }

            System.String key = PREF_PREFIX + "aac-" + mQuality.samplingRate;

            if (mSettings != null && mSettings.Contains(key))
            {
                System.String[] s = mSettings.GetString(key, "").Split(',');
                mQuality.samplingRate = (int)Integer.ValueOf(s[0]);
                mConfig = (int)Integer.ValueOf(s[1]);
                mChannel = (int)Integer.ValueOf(s[2]);
                return;
            }

            System.String TESTFILE = Android.OS.Environment.ExternalStorageDirectory.AbsolutePath + "/spydroid-test.adts";

            if (!Android.OS.Environment.ExternalStorageState.Equals(Android.OS.Environment.MediaMounted))
            {
                throw new IllegalStateException("No external storage or external storage not ready !");
            }

            // The structure of an ADTS packet is described here: http://wiki.multimedia.cx/index.php?title=ADTS

            // ADTS header is 7 or 9 bytes long
            byte[] buffer = new byte[9];

            mMediaRecorder = new MediaRecorder();
            mMediaRecorder.SetAudioSource((AudioSource)mAudioSource);
            mMediaRecorder.SetOutputFormat((OutputFormat)mOutputFormat);
            mMediaRecorder.SetAudioEncoder((AudioEncoder)mAudioEncoder);
            mMediaRecorder.SetAudioChannels(1);
            mMediaRecorder.SetAudioSamplingRate(mQuality.samplingRate);
            mMediaRecorder.SetAudioEncodingBitRate(mQuality.bitRate);
            mMediaRecorder.SetOutputFile(TESTFILE);
            mMediaRecorder.SetMaxDuration(1000);
            mMediaRecorder.Prepare();
            mMediaRecorder.Start();

            // We record for 1 sec
            // TODO: use the MediaRecorder.OnInfoListener
            try
            {
                Thread.Sleep(2000);
            }
            catch (InterruptedException e) { }

            mMediaRecorder.Stop();
            mMediaRecorder.Release();
            mMediaRecorder = null;

            File file = new File(TESTFILE);
            RandomAccessFile raf = new RandomAccessFile(file, "r");

            // ADTS packets start with a sync word: 12bits set to 1
            while (true)
            {
                if ((raf.ReadByte() & 0xFF) == 0xFF)
                {
                    buffer[0] = (byte)raf.ReadByte();
                    if ((buffer[0] & 0xF0) == 0xF0) break;
                }
            }

            raf.Read(buffer, 1, 5);

            mSamplingRateIndex = (buffer[1] & 0x3C) >> 2;
            mProfile = ((buffer[1] & 0xC0) >> 6) + 1;
            mChannel = (buffer[1] & 0x01) << 2 | (buffer[2] & 0xC0) >> 6;
            mQuality.samplingRate = AUDIO_SAMPLING_RATES[mSamplingRateIndex];

            // 5 bits for the object type / 4 bits for the sampling rate / 4 bits for the channel / padding
            mConfig = (mProfile & 0x1F) << 11 | (mSamplingRateIndex & 0x0F) << 7 | (mChannel & 0x0F) << 3;

            Log.Info(TAG, "MPEG VERSION: " + ((buffer[0] & 0x08) >> 3));
            Log.Info(TAG, "PROTECTION: " + (buffer[0] & 0x01));
            Log.Info(TAG, "PROFILE: " + AUDIO_OBJECT_TYPES[mProfile]);
            Log.Info(TAG, "SAMPLING FREQUENCY: " + mQuality.samplingRate);
            Log.Info(TAG, "CHANNEL: " + mChannel);

            raf.Close();

            if (mSettings != null)
            {
                ISharedPreferencesEditor editor = mSettings.Edit();
                editor.PutString(key, mQuality.samplingRate + "," + mConfig + "," + mChannel);
                editor.Commit();
            }

            if (!file.Delete()) Log.Error(TAG, "Temp file could not be erased");

        }

    }
}
