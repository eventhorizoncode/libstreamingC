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

using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Preferences;
using Android.Util;
using Java.IO;
using Java.Lang;
using Java.Nio;
using Java.Util.Concurrent;
using Java.Util;
using System;
using System.Collections;
using System.Collections.Generic;

using static Android.Media.MediaCodec;

namespace Net.Majorkernelpanic.Streaming
{

    /**
     * Parse an mp4 file.
     * An mp4 file contains a tree where each node has a name and a size.
     * This class is used by H264Stream.java to determine the SPS and PPS parameters of a short video recorded by the phone.
     */
    public class MP4Parser {

        private const System.String TAG = "MP4Parser";

        private Dictionary<System.String, Long> mBoxes = new Dictionary<System.String, Long>();
        private RandomAccessFile mFile;
        private long mPos = 0;


        /** Parses the mp4 file. **/
        public static MP4Parser parse(System.String path) {

            return new MP4Parser(path);
        }

        private MP4Parser(System.String path) {
            mFile = new RandomAccessFile(new File(path), "r");
            try {
                parse("", mFile.Length());
            } catch (Java.Lang.Exception e) {
                e.PrintStackTrace();
                throw new IOException("Parse error: malformed mp4 file");
            }
        }

        public void close() {
            try {
                mFile.Close();
            } catch (Java.Lang.Exception e) { };
        }

        public long getBoxPos(System.String box) {
            Long r = (Long)0;

            mBoxes.TryGetValue(box, out r);

            if (r == null)
                throw new IOException("Box not found: " + box);

            return (long)r;
        }

        public StsdBox getStsdBox() {
            try {

                return new StsdBox(mFile, getBoxPos("/moov/trak/mdia/minf/stbl/stsd"));
            } catch (IOException e) {
                throw new IOException("stsd box could not be found");
            }
        }

        private void parse(System.String path, long len) {

            ByteBuffer byteBuffer;
            long sum = 0, newlen = 0;
            byte[] buffer = new byte[8];
            Java.Lang.String name = null;

            if (!path.Equals("")) mBoxes.Add(path, (Long)(mPos - 8));

            while (sum < len) {
                mFile.Read(buffer, 0, 8);
                mPos += 8; sum += 8;

                if (validBoxName(buffer)) {
                    name = new Java.Lang.String(buffer, 4, 4);

                    if (buffer[3] == 1) {
                        // 64 bits atom size
                        mFile.Read(buffer, 0, 8);
                        mPos += 8; sum += 8;
                        byteBuffer = ByteBuffer.Wrap(buffer, 0, 8);
                        newlen = byteBuffer.GetLong() - 16;
                    } else {
                        // 32 bits atom size
                        byteBuffer = ByteBuffer.Wrap(buffer, 0, 4);
                        newlen = byteBuffer.GetInt() - 8;
                    }

                    // 1061109559+8 correspond to "????" in ASCII the HTC Desire S seems to write that sometimes, maybe other phones do
                    // "wide" atom would produce a newlen == 0, and we shouldn't throw an exception because of that
                    if (newlen < 0 || newlen == 1061109559) throw new IOException();

                    Log.d(TAG, "Atom -> name: " + name + " position: " + mPos + ", length: " + newlen);
                    sum += newlen;
                    parse(path + '/' + name, newlen);

                }
                else {
                    if (len < 8) {
                        mFile.Seek(mFile.FilePointer - 8 + len);
                        sum += len - 8;
                    } else {
                        int skipped = mFile.SkipBytes((int)(len - 8));
                        if (skipped < ((int)(len - 8))) {
                            throw new IOException();
                        }
                        mPos += len - 8;
                        sum += len - 8;
                    }
                }
            }
        }

        private bool validBoxName(byte[] buffer) {
            for (int i = 0; i < 4; i++) {
                // If the next 4 bytes are neither lowercase letters nor numbers
                if ((buffer[i + 4] < 'a' || buffer[i + 4] > 'z') && (buffer[i + 4] < '0' || buffer[i + 4] > '9')) return false;
            }
            return true;
        }

        public static System.String toHexString(byte[] buffer, int start, int len) {
            System.String c;
            StringBuilder s = new StringBuilder();
            for (int i = start; i < start + len; i++) {
                c = Integer.ToHexString(buffer[i] & 0xFF);
                s.Append(c.Length < 2 ? "0" + c : c);
            }
            return s.ToString();
        }

    }

    public class StsdBox {

        private RandomAccessFile fis;
        private byte[] buffer = new byte[4];
        private long pos = 0;

        private byte[] pps;
        private byte[] sps;
        private int spsLength, ppsLength;

        /** Parse the sdsd box in an mp4 file
         * fis: proper mp4 file
         * pos: stsd box's position in the file
         */
        public StsdBox(RandomAccessFile fis, long pos) {

            this.fis = fis;
            this.pos = pos;

            findBoxAvcc();
            findSPSandPPS();

        }

        public System.String getProfileLevel() {
            return MP4Parser.toHexString(sps, 1, 3);
        }

        public System.String getB64PPS() {
            return Android.Util.Base64.EncodeToString(pps, 0, ppsLength, Base64Flags.NoWrap);
        }

        public System.String getB64SPS() {
            return Android.Util.Base64.EncodeToString(sps, 0, spsLength, Base64Flags.NoWrap);
        }

        private bool findSPSandPPS() {
            /*
             *  SPS and PPS parameters are stored in the avcC box
             *  You may find really useful information about this box 
             *  in the document ISO-IEC 14496-15, part 5.2.4.1.1
             *  The box's structure is described there
             *  <pre>
             *  aligned(8) class AVCDecoderConfigurationRecord {
             *		unsigned int(8) configurationVersion = 1;
             *		unsigned int(8) AVCProfileIndication;
             *		unsigned int(8) profile_compatibility;
             *		unsigned int(8) AVCLevelIndication;
             *		bit(6) reserved = ‘111111’b;
             *		unsigned int(2) lengthSizeMinusOne;
             *		bit(3) reserved = ‘111’b;
             *		unsigned int(5) numOfSequenceParameterSets;
             *		for (i=0; i< numOfSequenceParameterSets; i++) {
             *			unsigned int(16) sequenceParameterSetLength ;
             *			bit(8*sequenceParameterSetLength) sequenceParameterSetNALUnit;
             *		}
             *		unsigned int(8) numOfPictureParameterSets;
             *		for (i=0; i< numOfPictureParameterSets; i++) {
             *			unsigned int(16) pictureParameterSetLength;
             *			bit(8*pictureParameterSetLength) pictureParameterSetNALUnit;
             *		}
             *	}
             *  </pre>
             */
            try {

                // TODO: Here we assume that numOfSequenceParameterSets = 1, numOfPictureParameterSets = 1 !
                // Here we extract the SPS parameter
                fis.SkipBytes(7);
                spsLength = 0xFF & fis.ReadByte();
                sps = new byte[spsLength];
                fis.Read(sps, 0, spsLength);
                // Here we extract the PPS parameter
                fis.SkipBytes(2);
                ppsLength = 0xFF & fis.ReadByte();
                pps = new byte[ppsLength];
                fis.Read(pps, 0, ppsLength);

            } catch (IOException e) {
                return false;
            }

            return true;
        }

        private bool findBoxAvcc() {
            try {
                fis.Seek(pos + 8);
                while (true) {
                    while (fis.Read() != 'a') ;
                    fis.Read(buffer, 0, 3);
                    if (buffer[0] == 'v' && buffer[1] == 'c' && buffer[2] == 'C') break;
                }
            } catch (IOException e) {
                return false;
            }
            return true;

        }

    }

}