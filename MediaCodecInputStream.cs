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
using Java.Nio;
/**
* A class for streaming AAC from the camera of an android device using RTP.
* You should use a {@link Session} instantiated with {@link SessionBuilder} instead of using this class directly.
* Call {@link #setDestinationAddress(InetAddress)}, {@link #setDestinationPorts(int)} and {@link #setAudioQuality(AudioQuality)}
* to configure the stream. You can then call {@link #start()} to start the RTP stream.
* Call {@link #stop()} to stop the stream.
*/
namespace Net.Majorkernelpanic.Streaming
{

    /**
     * An InputStream that uses data from a MediaCodec.
     * The purpose of this class is to interface existing RTP packetizers of
     * libstreaming with the new MediaCodec API. This class is not thread safe !  
     */
    public class MediaCodecInputStream : InputStream
    {

        public const System.String TAG = "MediaCodecInputStream";

        private MediaCodec mMediaCodec = null;
        private MediaCodec.BufferInfo mBufferInfo = new MediaCodec.BufferInfo();
        private ByteBuffer[] mBuffers = null;
        private ByteBuffer mBuffer = null;
        private int mIndex = -1;
        private bool mClosed = false;

        public MediaFormat mMediaFormat;

        public MediaCodecInputStream(MediaCodec mediaCodec)
        {
            mMediaCodec = mediaCodec;
            mBuffers = mMediaCodec.GetOutputBuffers();
        }

        public void close()
        {
            mClosed = true;
        }

        public int Read(byte[] buffer)
        {
            return 0;
        }
        public override int Read()
        {
            return 0;
        }

        public int Read(byte[] buffer, int offset, int length)
        {
            int min = 0;

            try
            {
                if (mBuffer == null)
                {
                    while (!Thread.Interrupted() && !mClosed)
                    {
                        mIndex = mMediaCodec.DequeueOutputBuffer(mBufferInfo, 500000);
                        if (mIndex >= 0)
                        {
                            //Log.d(TAG,"Index: "+mIndex+" Time: "+mBufferInfo.presentationTimeUs+" size: "+mBufferInfo.size);
                            mBuffer = mBuffers[mIndex];
                            mBuffer.Position(0);
                            break;
                        }
                        else if (mIndex == (int)MediaCodec.InfoOutputBuffersChanged)
                        {
                            mBuffers = mMediaCodec.GetOutputBuffers();
                        }
                        else if (mIndex == (int)MediaCodec.InfoOutputFormatChanged)
                        {
                            mMediaFormat = mMediaCodec.GetOutputFormat(mIndex);
                            Log.i(TAG, mMediaFormat.ToString());
                        }
                        else if (mIndex == (int)MediaCodec.InfoTryAgainLater)
                        {
                            Log.v(TAG, "No buffer available...");
                            //return 0;
                        }
                        else
                        {
                            Log.e(TAG, "Message: " + mIndex);
                            //return 0;
                        }
                    }
                }

                if (mClosed) throw new IOException("This InputStream was closed");

                min = length < mBufferInfo.Size - mBuffer.Position() ? length : mBufferInfo.Size - mBuffer.Position();
                mBuffer.Get(buffer, offset, min);
                if (mBuffer.Position() >= mBufferInfo.Size)
                {
                    mMediaCodec.ReleaseOutputBuffer(mIndex, false);
                    mBuffer = null;
                }

            }
            catch (RuntimeException e)
            {
                e.PrintStackTrace();
            }

            return min;
        }

        public int available()
        {
            if (mBuffer != null)
                return mBufferInfo.Size - mBuffer.Position();
            else
                return 0;
        }

        public MediaCodec.BufferInfo getLastBufferInfo()
        {
            return mBufferInfo;
        }

    }  
}
