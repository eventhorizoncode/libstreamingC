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
using Java.IO;
using Java.Lang;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Net.Majorkernelpanic.Streaming
{

    public class CodecManager {

        public const System.String TAG = "CodecManager";

        public static int[] SUPPORTED_COLOR_FORMATS = {
        (int)MediaCodecInfo.CodecCapabilities.COLORFormatYUV420SemiPlanar,
        (int)MediaCodecInfo.CodecCapabilities.COLORFormatYUV420PackedSemiPlanar,
        (int)MediaCodecInfo.CodecCapabilities.COLORFormatYUV420Planar,
        (int)MediaCodecInfo.CodecCapabilities.COLORFormatYUV420PackedPlanar,
        (int)MediaCodecInfo.CodecCapabilities.COLORTIFormatYUV420PackedSemiPlanar
    };

        private static Codec[] sEncoders = null;
        private static Codec[] sDecoders = null;

        public class Codec {
            public Codec(System.String name, Integer[] formats) {
                this.name = name;
                this.formats = formats;
            }
            public System.String name;
            public Integer[] formats;
        }

        /**
         * Lists all encoders that claim to support a color format that we know how to use.
         * @return A list of those encoders
         */
        // @SuppressLint("NewApi")

        public static System.Object lockEncoder = new System.Object();
        public static System.Object lockDecoder = new System.Object();
        public static  Codec[] findEncodersForMimeType(System.String mimeType) {
            lock (lockEncoder)
            {
                if (sEncoders != null) return sEncoders;

                List<Codec> encoders = new List<Codec>();

                // We loop through the encoders, apparently this can take up to a sec (testes on a GS3)
                for (int j = MediaCodecList.CodecCount - 1; j >= 0; j--)
                {
                    MediaCodecInfo codecInfo = MediaCodecList.GetCodecInfoAt(j);
                    if (!codecInfo.IsEncoder) continue;

                   System.String[] types = codecInfo.GetSupportedTypes();
                    for (int i = 0; i < types.Length; i++)
                    {
                        if (types[i].Equals(mimeType,StringComparison.CurrentCultureIgnoreCase))
                        {
                            try
                            {
                                MediaCodecInfo.CodecCapabilities capabilities = codecInfo.GetCapabilitiesForType(mimeType);
                                ISet<Integer> formats = new HashSet<Integer>();

                                // And through the color formats supported
                                for (int k = 0; k < capabilities.ColorFormats.Count; k++)
                                {
                                    int format = capabilities.ColorFormats[k];

                                    for (int l = 0; l < SUPPORTED_COLOR_FORMATS.Length; l++)
                                    {
                                        if (format == SUPPORTED_COLOR_FORMATS[l])
                                        {
                                            formats.Add((Integer)format);
                                        }
                                    }
                                }

                                Integer[] ar = new Integer[formats.Count];

                                formats.CopyTo(ar, 0);

                                Codec codec = new Codec(codecInfo.Name, ar);
                                encoders.Add(codec);
                            }
                            catch (Java.Lang.Exception e)
                            {
                                Log.Wtf(TAG, e);
                            }
                        }
                    }
                }
                sEncoders = new Codec[encoders.Count];

                //sEncoders = (Codec[])encoders.CopyTo(.ToArray(new Codec[encoders.Count]);
                encoders.CopyTo(sEncoders);
            }

            return sEncoders;

        }

        /**
         * Lists all decoders that claim to support a color format that we know how to use.
         * @return A list of those decoders
         */
      //  @SuppressLint("NewApi")

        public static Codec[] findDecodersForMimeType(System.String mimeType) {
            lock (lockDecoder)
            {
                if (sDecoders != null) return sDecoders;
                List<Codec> decoders = new List<Codec>();

                // We loop through the decoders, apparently this can take up to a sec (testes on a GS3)
                for (int j = MediaCodecList.CodecCount - 1; j >= 0; j--)
                {
                    MediaCodecInfo codecInfo = MediaCodecList.GetCodecInfoAt(j);
                    if (codecInfo.IsEncoder) continue;

                    System.String[] types = codecInfo.GetSupportedTypes();
                    for (int i = 0; i < types.Length; i++)
                    {
                        if (types[i].Equals(mimeType))
                        {
                            try
                            {
                                MediaCodecInfo.CodecCapabilities capabilities = codecInfo.GetCapabilitiesForType(mimeType);
                                ISet<Integer> formats = new HashSet<Integer>();

                                // And through the color formats supported
                                for (int k = 0; k < capabilities.ColorFormats.Count; k++)
                                {
                                    int format = capabilities.ColorFormats[k];

                                    for (int l = 0; l < SUPPORTED_COLOR_FORMATS.Length; l++)
                                    {
                                        if (format == SUPPORTED_COLOR_FORMATS[l])
                                        {
                                            formats.Add((Integer)format);
                                        }
                                    }
                                }

                                Integer[] frt = new Integer[formats.Count];

                                formats.CopyTo(frt,0);

                                Codec codec = new Codec(codecInfo.Name, frt);
                                decoders.Add(codec);
                            }
                            catch (Java.Lang.Exception e)
                            {
                                Log.Wtf(TAG, e);
                            }
                        }
                    }
                }

                sDecoders = new Codec[decoders.Count];
                decoders.CopyTo(sDecoders);


                // We will use the decoder from google first, it seems to work properly on many phones
                for (int i = 0; i < sDecoders.Length; i++)
                {
                    if (sDecoders[i].name.Equals("omx.google.h264.decoder"))
                    {
                        Codec codec = sDecoders[0];
                        sDecoders[0] = sDecoders[i];
                        sDecoders[i] = codec;
                    }
                }

                return sDecoders;
            }
        }

    }

}