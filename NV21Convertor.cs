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



using Java.Nio;
using Android.Media;
using Android.Util;

namespace Net.Majorkernelpanic.Streaming.Hw
{


    /**
     * Converts from NV21 to YUV420 semi planar or planar.
     */
    public class NV21Convertor
    {

        private int mSliceHeight, mHeight;
        private int mStride, mWidth;
        private int mSize;
        private bool mPlanar, mPanesReversed = false;
        private int mYPadding;
        private byte[] mBuffer;
        ByteBuffer mCopy;

        public void setSize(int width, int height)
        {
            mHeight = height;
            mWidth = width;
            mSliceHeight = height;
            mStride = width;
            mSize = mWidth * mHeight;
        }

        public void setStride(int width)
        {
            mStride = width;
        }

        public void setSliceHeigth(int height)
        {
            mSliceHeight = height;
        }

        public void setPlanar(bool planar)
        {
            mPlanar = planar;
        }

        public void setYPadding(int padding)
        {
            mYPadding = padding;
        }

        public int getBufferSize()
        {
            return 3 * mSize / 2;
        }

        public void setEncoderColorFormat(int colorFormat)
        {
            switch (colorFormat)
            {
                case (int)MediaCodecCapabilities.Formatyuv420semiplanar:
                case (int)MediaCodecCapabilities.Formatyuv420packedsemiplanar:
                case (int)MediaCodecCapabilities.TiFormatyuv420packedsemiplanar:
                    setPlanar(false);
                    break;
                case (int)MediaCodecCapabilities.Formatyuv420planar:
                case (int)MediaCodecCapabilities.Formatyuv420packedplanar:
                    setPlanar(true);
                    break;
            }
        }

        public void setColorPanesReversed(bool b)
        {
            mPanesReversed = b;
        }

        public int getStride()
        {
            return mStride;
        }

        public int getSliceHeigth()
        {
            return mSliceHeight;
        }

        public int getYPadding()
        {
            return mYPadding;
        }


        public bool getPlanar()
        {
            return mPlanar;
        }

        public bool getUVPanesReversed()
        {
            return mPanesReversed;
        }

        public void convert(byte[] data, ByteBuffer buffer)
        {
            byte[] result = convert(data);
            int min = buffer.Capacity() < data.Length ? buffer.Capacity() : data.Length;
            buffer.Put(result, 0, min);
        }

        public byte[] convert(byte[] data)
        {

            // A buffer large enough for every case
            if (mBuffer == null || mBuffer.Length != 3 * mSliceHeight * mStride / 2 + mYPadding)
            {
                mBuffer = new byte[3 * mSliceHeight * mStride / 2 + mYPadding];
            }

            if (!mPlanar)
            {
                if (mSliceHeight == mHeight && mStride == mWidth)
                {
                    // Swaps U and V
                    if (!mPanesReversed)
                    {
                        for (int i = mSize; i < mSize + mSize / 2; i += 2)
                        {
                            mBuffer[0] = data[i + 1];
                            data[i + 1] = data[i];
                            data[i] = mBuffer[0];
                        }
                    }
                    if (mYPadding > 0)
                    {
                        System.Array.Copy(data, 0, mBuffer, 0, mSize);
                        System.Array.Copy(data, mSize, mBuffer, mSize + mYPadding, mSize / 2);
                        return mBuffer;
                    }
                    return data;
                }
            }
            else
            {
                if (mSliceHeight == mHeight && mStride == mWidth)
                {
                    // De-interleave U and V
                    if (!mPanesReversed)
                    {
                        for (int i = 0; i < mSize / 4; i += 1)
                        {
                            mBuffer[i] = data[mSize + 2 * i + 1];
                            mBuffer[mSize / 4 + i] = data[mSize + 2 * i];
                        }
                    }
                    else
                    {
                        for (int i = 0; i < mSize / 4; i += 1)
                        {
                            mBuffer[i] = data[mSize + 2 * i];
                            mBuffer[mSize / 4 + i] = data[mSize + 2 * i + 1];
                        }
                    }
                    if (mYPadding == 0)
                    {
                        System.Array.Copy(mBuffer, 0, data, mSize, mSize / 2);
                    }
                    else
                    {
                        System.Array.Copy(data, 0, mBuffer, 0, mSize);
                        System.Array.Copy(mBuffer, 0, mBuffer, mSize + mYPadding, mSize / 2);
                        return mBuffer;
                    }
                    return data;
                }
            }

            return data;
        }

    }
}
