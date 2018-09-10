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
 * 
 *   RFC 3984.
 *   
 *   H.264 streaming over RTP.
 *   
 *   Must be fed with an InputStream containing H.264 NAL units preceded by their length (4 bytes).
 *   The stream must start with mpeg4 or 3gpp header, it will be skipped.
 *   
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
    public class H264Packetizer : AbstractPacketizer ,IRunnable
    {

	    public const System.String TAG = "H264Packetizer";

	    private Thread t = null;
	    private int naluLength = 0;
	    private long delay = 0, oldtime = 0;
	    private Statistics stats = new Statistics();
	    private byte[] sps = null, pps = null, stapa = null;
	    byte[] header = new byte[5];	
	    private int count = 0;
	    private int streamType = 1;

        public IntPtr Handle => throw new NotImplementedException();

        public H264Packetizer():base() {
		    
		    socket.setClockFrequency(90000);
	    }

	    public override void start() {
		    if (t == null) {
			    t = new Thread(this);
			    t.Start();
		    }
	    }

	    public override  void stop() {
		    if (t != null) {
			    try {
				    inputStream.Close();
			    } catch (IOException e) {}
			    t.Interrupt();
			    try {
				    t.Join();
			    } catch (InterruptedException e) {}
			    t = null;
		    }
	    }

	    public void setStreamParameters(byte[] pps, byte[] sps) {
		    this.pps = pps;
		    this.sps = sps;

		    // A STAP-A NAL (NAL type 24) containing the sps and pps of the stream
		    if (pps != null && sps != null) {
			    // STAP-A NAL header + NALU 1 (SPS) size + NALU 2 (PPS) size = 5 bytes
			    stapa = new byte[sps.Length + pps.Length + 5];

			    // STAP-A NAL header is 24
			    stapa[0] = 24;

			    // Write NALU 1 size into the array (NALU 1 is the SPS).
			    stapa[1] = (byte) (sps.Length >> 8);
			    stapa[2] = (byte) (sps.Length & 0xFF);

			    // Write NALU 2 size into the array (NALU 2 is the PPS).
			    stapa[sps.Length + 3] = (byte) (pps.Length >> 8);
			    stapa[sps.Length + 4] = (byte) (pps.Length & 0xFF);

			    // Write NALU 1 into the array, then write NALU 2 into the array.
			    JavaSystem.Arraycopy(sps, 0, stapa, 3, sps.Length);
                JavaSystem.Arraycopy(pps, 0, stapa, 5 + sps.Length, pps.Length);
		    }
	    }	

	    public void Run() {
		    long duration = 0;
		    Log.d(TAG,"H264 packetizer started !");
		    stats.reset();
		    count = 0;

		    if (inputStream.GetType()== typeof(MediaCodecInputStream)) {
			    streamType = 1;
			    socket.setCacheSize(0);
		    } else {
			    streamType = 0;	
			    socket.setCacheSize(400);
		    }

		    try {
			    while (!Thread.Interrupted()) {

				    oldtime = JavaSystem.NanoTime();
				    // We read a NAL units from the input stream and we send them
				    send();
				    // We measure how long it took to receive NAL units from the phone
				    duration = JavaSystem.NanoTime() - oldtime;

				    stats.push(duration);
				    // Computes the average duration of a NAL unit
				    delay = stats.average();
				    //Log.d(TAG,"duration: "+duration/1000000+" delay: "+delay/1000000);

			    }
		    } catch (IOException e) {
		    } catch (InterruptedException e) {}

		    Log.Debug(TAG,"H264 packetizer stopped !");

	    }

	    /**
	     * Reads a NAL unit in the FIFO and sends it.
	     * If it is too big, we split it in FU-A units (RFC 3984).
	     */
	    
	    private void send() {
		    int sum = 1, len = 0, type;

		    if (streamType == 0) {
			    // NAL units are preceeded by their length, we parse the length
			    fill(header,0,5);
			    ts += delay;
			    naluLength = header[3]&0xFF | (header[2]&0xFF)<<8 | (header[1]&0xFF)<<16 | (header[0]&0xFF)<<24;
			    if (naluLength>100000 || naluLength<0) resync();
		    } else if (streamType == 1) {
			    // NAL units are preceeded with 0x00000001
			    fill(header,0,5);
			    ts = ((MediaCodecInputStream)inputStream).getLastBufferInfo().PresentationTimeUs*1000L;
			    //ts += delay;
			    naluLength = inputStream.Available()+1;
			    if (!(header[0]==0 && header[1]==0 && header[2]==0)) {
				    // Turns out, the NAL units are not preceeded with 0x00000001
				    Log.e(TAG, "NAL units are not preceeded by 0x00000001");
				    streamType = 2; 
				    return;
			    }
		    } else {
			    // Nothing preceededs the NAL units
			    fill(header,0,1);
			    header[4] = header[0];
			    ts = ((MediaCodecInputStream)inputStream).getLastBufferInfo().PresentationTimeUs*1000L;
			    //ts += delay;
			    naluLength = inputStream.Available()+1;
		    }

		    // Parses the NAL unit type
		    type = header[4]&0x1F;


		    // The stream already contains NAL unit type 7 or 8, we don't need 
		    // to add them to the stream ourselves
		    if (type == 7 || type == 8) {
			    Log.v(TAG,"SPS or PPS present in the stream.");
			    count++;
			    if (count>4) {
				    sps = null;
				    pps = null;
			    }
		    }

		    // We send two packets containing NALU type 7 (SPS) and 8 (PPS)
		    // Those should allow the H264 stream to be decoded even if no SDP was sent to the decoder.
		    if (type == 5 && sps != null && pps != null) {
			    buffer = socket.requestBuffer();
			    socket.markNextPacket();
			    socket.updateTimestamp(ts);
			    JavaSystem.Arraycopy(stapa, 0, buffer, rtphl, stapa.Length);
			    base.send(rtphl+stapa.Length);
		    }

		    //Log.d(TAG,"- Nal unit length: " + naluLength + " delay: "+delay/1000000+" type: "+type);

		    // Small NAL unit => Single NAL unit 
		    if (naluLength<=MAXPACKETSIZE-rtphl-2) {
			    buffer = socket.requestBuffer();
			    buffer[rtphl] = header[4];
			    len = fill(buffer, rtphl+1,  naluLength-1);
			    socket.updateTimestamp(ts);
			    socket.markNextPacket();
			    base.send(naluLength+rtphl);
			    //Log.d(TAG,"----- Single NAL unit - len:"+len+" delay: "+delay);
		    }
		    // Large NAL unit => Split nal unit 
		    else {

			    // Set FU-A header
			    header[1] = (byte) (header[4] & 0x1F);  // FU header type
			    header[1] += 0x80; // Start bit
			    // Set FU-A indicator
			    header[0] = (byte) ((header[4] & 0x60) & 0xFF); // FU indicator NRI
			    header[0] += 28;

			    while (sum < naluLength) {
				    buffer = socket.requestBuffer();
				    buffer[rtphl] = header[0];
				    buffer[rtphl+1] = header[1];
				    socket.updateTimestamp(ts);
				    if ((len = fill(buffer, rtphl+2,  naluLength-sum > MAXPACKETSIZE-rtphl-2 ? MAXPACKETSIZE-rtphl-2 : naluLength-sum  ))<0) return; sum += len;
				    // Last packet before next NAL
				    if (sum >= naluLength) {
					    // End bit on
					    buffer[rtphl+1] += 0x40;
					    socket.markNextPacket();
				    }
				    base.send(len+rtphl+2);
				    // Switch start bit
				    header[1] = (byte) (header[1] & 0x7F); 
				    //Log.d(TAG,"----- FU-A unit, sum:"+sum);
			    }
		    }
	    }

	    private int fill(byte[] buffer, int offset,int length) {
		    int sum = 0, len;
		    while (sum<length) {
			    len = inputStream.Read(buffer, offset+sum, length-sum);
			    if (len<0) {
				    throw new IOException("End of stream");
			    }
			    else sum+=len;
		    }
		    return sum;
	    }

	    private void resync() {
		    int type;

		    Log.Error(TAG,"Packetizer out of sync ! Let's try to fix that...(NAL length: "+naluLength+")");

		    while (true) {

			    header[0] = header[1];
			    header[1] = header[2];
			    header[2] = header[3];
			    header[3] = header[4];
			    header[4] = (byte) inputStream.Read();

			    type = header[4]&0x1F;

			    if (type == 5 || type == 1) {
				    naluLength = header[3]&0xFF | (header[2]&0xFF)<<8 | (header[1]&0xFF)<<16 | (header[0]&0xFF)<<24;
				    if (naluLength>0 && naluLength<100000) {
					    oldtime = JavaSystem.NanoTime();
					    Log.Error(TAG,"A NAL unit may have been found in the bit stream !");
					    break;
				    }
				    if (naluLength==0) {
					    Log.Error(TAG,"NAL unit with NULL size found...");
				    } else if (header[3]==0xFF && header[2]==0xFF && header[1]==0xFF && header[0]==0xFF) {
					    Log.Error(TAG,"NAL unit with 0xFFFFFFFF size found...");
				    }
			    }

		    }

	    }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }

}