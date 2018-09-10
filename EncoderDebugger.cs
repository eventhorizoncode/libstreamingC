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
 * The purpose of this class is to detect and by-pass some bugs (or underspecified configuration) that
 * encoders available through the MediaCodec API may have. <br />
 * Feeding the encoder with a surface is not tested here.
 * Some bugs you may have encountered:<br />
 * <ul>
 * <li>U and V panes reversed</li>
 * <li>Some padding is needed after the Y pane</li>
 * <li>stride!=width or slice-height!=height</li>
 * </ul>
 */
using Android.Content;
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
using System.Threading;
using static Android.Media.MediaCodec;

namespace Net.Majorkernelpanic.Streaming
{
        public class EncoderDebugger {

	    public const  System.String TAG = "EncoderDebugger";

	    /** Prefix that will be used for all shared preferences saved by libstreaming. */
	    private const   System.String PREF_PREFIX = "libstreaming-";

	    /** 
	     * If this is set to false the test will be run only once and the result 
	     * will be saved in the shared preferences. 
	     */
	    private const bool DEBUG = false;
	
	    /** Set this to true to see more logs. */
	    private const bool VERBOSE = false;

	    /** Will be incremented every time this test is modified. */
	    private const int VERSION = 3;

	    /** Bit rate that will be used with the encoder. */
	    private const int BITRATE = 1000000;

	    /** Frame rate that will be used to test the encoder. */
	    private const int FRAMERATE = 20;

	    private const System.String MIME_TYPE = "video/avc";

	    private const int NB_DECODED = 34;
	    private const int NB_ENCODED = 50;

	    private int mDecoderColorFormat, mEncoderColorFormat;
	    private System.String mDecoderName, mEncoderName, mErrorLog;
	    private MediaCodec mEncoder, mDecoder;
	    private int mWidth, mHeight, mSize;
	    private byte[] mSPS, mPPS;
	    private byte[] mData, mInitialImage;
	    private MediaFormat mDecOutputFormat;
	    private NV21Convertor mNV21;
	    private ISharedPreferences mPreferences;
	    private byte[][] mVideo, mDecodedVideo;
	    private System.String mB64PPS, mB64SPS;


        private static int debugWidth = 0;
        private static Context debugContext = null;
        private static int debugHeight = 0;
        public  static void asyncDebug(Context context, int width, int height) {

            debugWidth = width;
            debugHeight = height;
            debugContext = context;

            System.Threading.Thread thread = new System.Threading.Thread(new ThreadStart(DebugStuff));
            thread.Start();
          /*  new Thread(new Runnable() {
			    public void run() {
				    
			    }
		    }).start();*/
	    }

        public static void DebugStuff()
        {
            try
            {
                ISharedPreferences prefs = PreferenceManager.GetDefaultSharedPreferences(debugContext);
                debug(prefs, debugWidth, debugHeight);
            }
            catch (Java.Lang.Exception e)
            {
            }
        }

	
	    public static EncoderDebugger debug(Context context, int width, int height) {
		    ISharedPreferences prefs = PreferenceManager.GetDefaultSharedPreferences(context);
		    return debug(prefs, width, height);
	    }

	    public static EncoderDebugger debug(ISharedPreferences prefs, int width, int height) {
		    EncoderDebugger debugger = new EncoderDebugger(prefs, width, height);
		    debugger.debug();
		    return debugger;
	    }

	    public System.String getB64PPS() {
		    return mB64PPS;
	    }

	    public System.String getB64SPS() {
		    return mB64SPS;
	    }

	    public System.String getEncoderName() {
		    return mEncoderName;
	    }

	    public int getEncoderColorFormat() {
		    return mEncoderColorFormat;
	    }

	    /** This {@link NV21Convertor} will do the necessary work to feed properly the encoder. */
	    public NV21Convertor getNV21Convertor() {
		    return mNV21;
	    }

	    /** A log of all the errors that occurred during the test. */
	    public System.String getErrorLog() {
		    return mErrorLog;
	    }

	    private EncoderDebugger(ISharedPreferences prefs, int width, int height) {
		    mPreferences = prefs;
		    mWidth = width;
		    mHeight = height;
		    mSize = width*height;
		    reset();
	    }

	    private void reset() {
		    mNV21 = new NV21Convertor();
		    mVideo = new byte[NB_ENCODED][];
		    mDecodedVideo = new byte[NB_DECODED][];
		    mErrorLog = "";
		    mPPS = null;
		    mSPS = null;		
	    }

	    private void debug() {
		
		    // If testing the phone again is not needed, 
		    // we just restore the result from the shared preferences
		    if (!checkTestNeeded()) {
			    System.String resolution = mWidth+"x"+mHeight+"-";			

			    bool success = mPreferences.GetBoolean(PREF_PREFIX+resolution+"success",false);
			    if (!success) {
				    throw new RuntimeException("Phone not supported with this resolution ("+mWidth+"x"+mHeight+")");
			    }

			    mNV21.setSize(mWidth, mHeight);
			    mNV21.setSliceHeigth(mPreferences.GetInt(PREF_PREFIX+resolution+"sliceHeight", 0));
			    mNV21.setStride(mPreferences.GetInt(PREF_PREFIX+resolution+"stride", 0));
			    mNV21.setYPadding(mPreferences.GetInt(PREF_PREFIX+resolution+"padding", 0));
			    mNV21.setPlanar(mPreferences.GetBoolean(PREF_PREFIX+resolution+"planar", false));
			    mNV21.setColorPanesReversed(mPreferences.GetBoolean(PREF_PREFIX+resolution+"reversed", false));
			    mEncoderName = mPreferences.GetString(PREF_PREFIX+resolution+"encoderName", "");
			    mEncoderColorFormat = mPreferences.GetInt(PREF_PREFIX+resolution+"colorFormat", 0);
			    mB64PPS = mPreferences.GetString(PREF_PREFIX +resolution+"pps", "");
			    mB64SPS = mPreferences.GetString(PREF_PREFIX +resolution+"sps", "");

			    return;
		    }

		    if (VERBOSE) Log.d(TAG, ">>>> Testing the phone for resolution "+mWidth+"x"+mHeight);

            // Builds a list of available encoders and decoders we may be able to use
            // because they support some nice color formats
            CodecManager.Codec[] encoders = CodecManager.findEncodersForMimeType(MIME_TYPE);
            CodecManager.Codec[] decoders = CodecManager.findDecodersForMimeType(MIME_TYPE);

		    int count = 0, n = 1;
		    for (int i=0;i<encoders.Length;i++) {
			    count += encoders[i].formats.Length;
		    }
		
		    // Tries available encoders
		    for (int i=0;i<encoders.Length;i++) {
			    for (int j=0;j<encoders[i].formats.Length;j++) {
				    reset();
				
				    mEncoderName = encoders[i].name;
				    mEncoderColorFormat = (int)encoders[i].formats[j];

				    if (VERBOSE) Log.v(TAG, ">> Test "+(n++)+"/"+count+": "+mEncoderName+" with color format "+mEncoderColorFormat+" at "+mWidth+"x"+mHeight);
				
				    // Converts from NV21 to YUV420 with the specified parameters
				    mNV21.setSize(mWidth, mHeight);
				    mNV21.setSliceHeigth(mHeight);
				    mNV21.setStride(mWidth);
				    mNV21.setYPadding(0);
				    mNV21.setEncoderColorFormat(mEncoderColorFormat);

				    // /!\ NV21Convertor can directly modify the input
				    createTestImage();
				    mData = mNV21.convert(mInitialImage);

				    try {

					    // Starts the encoder
					    configureEncoder();
					    searchSPSandPPS();
					
					    if (VERBOSE) Log.v(TAG, "SPS and PPS in b64: SPS="+mB64SPS+", PPS="+mB64PPS);

					    // Feeds the encoder with an image repeatedly to produce some NAL units
					    encode();

					    // We now try to decode the NALs with decoders available on the phone
					    bool decoded = false;
					    for (int k=0;k<decoders.Length && !decoded;k++) {
						    for (int l=0;l<decoders[k].formats.Length && !decoded;l++) {
							    mDecoderName = decoders[k].name;
							    mDecoderColorFormat = (int)decoders[k].formats[l];
							    try {
								    configureDecoder();
							    } catch (Java.Lang.Exception e) {
								    if (VERBOSE) Log.Debug(TAG, mDecoderName+" can't be used with "+mDecoderColorFormat+" at "+mWidth+"x"+mHeight);
								    releaseDecoder();
								    break;
							    }
							    try {
								    decode(true);
								    if (VERBOSE) Log.Debug(TAG, mDecoderName+" successfully decoded the NALs (color format "+mDecoderColorFormat+")");
								    decoded = true;
							    } catch (Java.Lang.Exception e) {
								    if (VERBOSE) Log.Error(TAG, mDecoderName+" failed to decode the NALs");
								    e.PrintStackTrace();
							    } finally {
								    releaseDecoder();
							    }
						    }
					    }

					    if (!decoded) throw new RuntimeException("Failed to decode NALs from the encoder.");

					    // Compares the image before and after
					    if (!compareLumaPanes()) {
						    // TODO: try again with a different stride
						    // TODO: try again with the "stride" param
						    throw new RuntimeException("It is likely that stride!=width");
					    }

					    int padding;
					    if ((padding = checkPaddingNeeded())>0) {
						    if (padding<4096) {
							    if (VERBOSE) Log.Debug(TAG, "Some padding is needed: "+padding);
							    mNV21.setYPadding(padding);
							    createTestImage();
							    mData = mNV21.convert(mInitialImage);
							    encodeDecode();
						    } else {
							    // TODO: try again with a different sliceHeight
							    // TODO: try again with the "slice-height" param
							    throw new RuntimeException("It is likely that sliceHeight!=height");
						    }
					    }

					    createTestImage();
					    if (!compareChromaPanes(false)) {
						    if (compareChromaPanes(true)) {
							    mNV21.setColorPanesReversed(true);
							    if (VERBOSE) Log.d(TAG, "U and V pane are reversed");
						    } else {
							    throw new RuntimeException("Incorrect U or V pane...");
						    }
					    }

					    saveTestResult(true);
					    Log.Verbose(TAG, "The encoder "+mEncoderName+" is usable with resolution "+mWidth+"x"+mHeight);
					    return;

				    } catch (Java.Lang.Exception e) {
					    StringWriter sw = new StringWriter();
					    PrintWriter pw = new PrintWriter(sw); e.PrintStackTrace(pw);
					    System.String stack = sw.ToString();
					    System.String str = "Encoder "+mEncoderName+" cannot be used with color format "+mEncoderColorFormat;
					    if (VERBOSE) Log.Error(TAG, str, e);
					    mErrorLog += str + "\n" + stack;
					    e.PrintStackTrace();
				    } finally {
					    releaseEncoder();
				    }

			    }
		    }

		    saveTestResult(false);
		    Log.e(TAG,"No usable encoder were found on the phone for resolution "+mWidth+"x"+mHeight);
		    throw new RuntimeException("No usable encoder were found on the phone for resolution "+mWidth+"x"+mHeight);

	    }

	    private bool checkTestNeeded() {
		    System.String resolution = mWidth+"x"+mHeight+"-";

		    // Forces the test
		    if (DEBUG || mPreferences==null) return true; 

		    // If the sdk has changed on the phone, or the version of the test 
		    // it has to be run again
		    if (mPreferences.Contains(PREF_PREFIX+resolution+"lastSdk")) {
			    int lastSdk = mPreferences.GetInt(PREF_PREFIX+resolution+"lastSdk", 0);
			    int lastVersion = mPreferences.GetInt(PREF_PREFIX+resolution+"lastVersion", 0);
			    if ((int)Build.VERSION.SdkInt>lastSdk || VERSION>lastVersion) {
				    return true;
			    }
		    } else {
			    return true;
		    }
		    return false;
	    }


	    /**
	     * Saves the result of the test in the shared preferences,
	     * we will run it again only if the SDK has changed on the phone,
	     * or if this test has been modified.
	     */	
	    private void saveTestResult(bool success) {
		    System.String resolution = mWidth+"x"+mHeight+"-";
		    ISharedPreferencesEditor editor = mPreferences.Edit();

		    editor.PutBoolean(PREF_PREFIX+resolution+"success", success);

		    if (success) {
			    editor.PutInt(PREF_PREFIX+resolution+"lastSdk", (int)Build.VERSION.SdkInt);
			    editor.PutInt(PREF_PREFIX+resolution+"lastVersion", VERSION);
			    editor.PutInt(PREF_PREFIX+resolution+"sliceHeight", mNV21.getSliceHeigth());
			    editor.PutInt(PREF_PREFIX+resolution+"stride", mNV21.getStride());
			    editor.PutInt(PREF_PREFIX+resolution+"padding", mNV21.getYPadding());
			    editor.PutBoolean(PREF_PREFIX+resolution+"planar", mNV21.getPlanar());
			    editor.PutBoolean(PREF_PREFIX+resolution+"reversed", mNV21.getUVPanesReversed());
			    editor.PutString(PREF_PREFIX+resolution+"encoderName", mEncoderName);
			    editor.PutInt(PREF_PREFIX+resolution+"colorFormat", mEncoderColorFormat);
			    editor.PutString(PREF_PREFIX+resolution+"encoderName", mEncoderName);
			    editor.PutString(PREF_PREFIX+resolution+"pps", mB64PPS);
			    editor.PutString(PREF_PREFIX+resolution+"sps", mB64SPS);
		    }

		    editor.Commit();
	    }

	    /**
	     * Creates the test image that will be used to feed the encoder.
	     */
	    private void createTestImage() {
		    mInitialImage = new byte[3*mSize/2];
		    for (int i=0;i<mSize;i++) {
			    mInitialImage[i] = (byte) (40+i%199);
		    }
		    for (int i=mSize;i<3*mSize/2;i+=2) {
			    mInitialImage[i] = (byte) (40+i%200);
			    mInitialImage[i+1] = (byte) (40+(i+99)%200);
		    }

	    }

	    /**
	     * Compares the Y pane of the initial image, and the Y pane
	     * after having encoded & decoded the image.
	     */
	    private bool compareLumaPanes() {
		    int d, e, f = 0;
		    for (int j=0;j<NB_DECODED;j++) {
			    for (int i=0;i<mSize;i+=10) {
				    d = (mInitialImage[i]&0xFF) - (mDecodedVideo[j][i]&0xFF);
				    e = (mInitialImage[i+1]&0xFF) - (mDecodedVideo[j][i+1]&0xFF);
				    d = d<0 ? -d : d;
				    e = e<0 ? -e : e;
				    if (d>50 && e>50) {
					    mDecodedVideo[j] = null;
					    f++;
					    break;
				    }
			    }
		    }
		    return f<=NB_DECODED/2;
	    }

	    private int checkPaddingNeeded() {
		    int i = 0, j = 3*mSize/2-1, max = 0;
		    int[] r = new int[NB_DECODED];
		    for (int k=0;k<NB_DECODED;k++) {
			    if (mDecodedVideo[k] != null) {
				    i = 0;
				    while (i<j && (mDecodedVideo[k][j-i]&0xFF)<50) i+=2;
				    if (i>0) {
					    r[k] = ((i>>6)<<6);
					    max = r[k]>max ? r[k] : max;
					    if (VERBOSE) Log.e(TAG,"Padding needed: "+r[k]);
				    } else {
					    if (VERBOSE) Log.v(TAG,"No padding needed.");
				    }
			    }
		    }

		    return ((max>>6)<<6);
	    }

	    /**
	     * Compares the U or V pane of the initial image, and the U or V pane
	     * after having encoded & decoded the image.
	     */	
	    private bool compareChromaPanes(bool crossed) {
		    int d, f = 0;

		    for (int j=0;j<NB_DECODED;j++) {
			    if (mDecodedVideo[j] != null) {
				    // We compare the U and V pane before and after
				    if (!crossed) {
					    for (int i=mSize;i<3*mSize/2;i+=1) {
						    d = (mInitialImage[i]&0xFF) - (mDecodedVideo[j][i]&0xFF);
						    d = d<0 ? -d : d;
						    if (d>50) {
							    //if (VERBOSE) Log.e(TAG,"BUG "+(i-mSize)+" d "+d);
							    f++;
							    break;
						    }
					    }

					    // We compare the V pane before with the U pane after
				    } else {
					    for (int i=mSize;i<3*mSize/2;i+=2) {
						    d = (mInitialImage[i]&0xFF) - (mDecodedVideo[j][i+1]&0xFF);
						    d = d<0 ? -d : d;
						    if (d>50) {
							    f++;
						    }
					    }
				    }
			    }
		    }
		    return f<=NB_DECODED/2;
	    }	

	    /**
	     * Converts the image obtained from the decoder to NV21.
	     */
	    private void convertToNV21(int k) {		
		    byte[] buffer = new byte[3*mSize/2];

		    int stride = mWidth, sliceHeight = mHeight;
		    int colorFormat = mDecoderColorFormat;
		    bool planar = false;

		    if (mDecOutputFormat != null) {
			    MediaFormat format = mDecOutputFormat;
			    if (format != null) {
				    if (format.ContainsKey("slice-height")) {
					    sliceHeight = format.GetInteger("slice-height");
					    if (sliceHeight<mHeight) sliceHeight = mHeight;
				    }
				    if (format.ContainsKey("stride")) {
					    stride = format.GetInteger("stride");
					    if (stride<mWidth) stride = mWidth;
				    }
				    if (format.ContainsKey(MediaFormat.KeyColorFormat) && format.GetInteger(MediaFormat.KeyColorFormat)>0) {
					    colorFormat = format.GetInteger(MediaFormat.KeyColorFormat);
				    }
			    }
		    }

		    switch ((MediaCodecCapabilities)colorFormat) {
		    case MediaCodecCapabilities.Formatyuv420semiplanar:
		    case MediaCodecCapabilities.Formatyuv420packedsemiplanar:
		    case MediaCodecCapabilities.TiFormatyuv420packedsemiplanar:
			    planar = false;
			    break;	
		    case MediaCodecCapabilities.Formatyuv420planar:
		    case MediaCodecCapabilities.Formatyuv420packedplanar:
			    planar = true;
			    break;
		    }

		    for (int i=0;i<mSize;i++) {
			    if (i%mWidth==0) i+=stride-mWidth;
			    buffer[i] = mDecodedVideo[k][i];
		    }

		    if (!planar) {
			    for (int i=0,j=0;j<mSize/4;i+=1,j+=1) {
				    if (i%mWidth/2==0) i+=(stride-mWidth)/2;
				    buffer[mSize+2*j+1] = mDecodedVideo[k][stride*sliceHeight+2*i];
				    buffer[mSize+2*j] = mDecodedVideo[k][stride*sliceHeight+2*i+1];
			    }
		    } else {
			    for (int i=0,j=0;j<mSize/4;i+=1,j+=1) {
				    if (i%mWidth/2==0) i+=(stride-mWidth)/2;
				    buffer[mSize+2*j+1] = mDecodedVideo[k][stride*sliceHeight+i];
				    buffer[mSize+2*j] = mDecodedVideo[k][stride*sliceHeight*5/4+i];
			    }
		    }

		    mDecodedVideo[k] = buffer;

	    }

	    /**
	     * Instantiates and starts the encoder.
	     * @throws IOException The encoder cannot be configured
	     */
	    private void configureEncoder() {
		    mEncoder = MediaCodec.CreateByCodecName(mEncoderName);
		    MediaFormat mediaFormat = MediaFormat.CreateVideoFormat(MIME_TYPE, mWidth, mHeight);
		    mediaFormat.SetInteger(MediaFormat.KeyBitRate, BITRATE);
		    mediaFormat.SetInteger(MediaFormat.KeyFrameRate, FRAMERATE);	
		    mediaFormat.SetInteger(MediaFormat.KeyColorFormat, mEncoderColorFormat);
		    mediaFormat.SetInteger(MediaFormat.KeyIFrameInterval, 1);
		    mEncoder.Configure(mediaFormat, null, null, MediaCodecConfigFlags.Encode);
		    mEncoder.Start();
	    }

	    private void releaseEncoder() {
		    if (mEncoder != null) {
			    try {
				    mEncoder.Stop();
			    } catch (Java.Lang.Exception ignore) {}
			    try {
				    mEncoder.Release();
			    } catch (Java.Lang.Exception ignore) {}
		    }
	    }

	    /**
	     * Instantiates and starts the decoder.
	     * @throws IOException The decoder cannot be configured
	     */	
	    private void configureDecoder() {
		    byte[] prefix = new byte[] {0x00,0x00,0x00,0x01};

		    ByteBuffer csd0 = ByteBuffer.Allocate(4+mSPS.Length+4+mPPS.Length);
		    csd0.Put(new byte[] {0x00,0x00,0x00,0x01});
		    csd0.Put(mSPS);
		    csd0.Put(new byte[] {0x00,0x00,0x00,0x01});
		    csd0.Put(mPPS);

		    mDecoder = MediaCodec.CreateByCodecName(mDecoderName);
		    MediaFormat mediaFormat = MediaFormat.CreateVideoFormat(MIME_TYPE, mWidth, mHeight);
		    mediaFormat.SetByteBuffer("csd-0", csd0);
		    mediaFormat.SetInteger(MediaFormat.KeyColorFormat, mDecoderColorFormat);
		    mDecoder.Configure(mediaFormat, null, null, 0);
		    mDecoder.Start();

		    ByteBuffer[] decInputBuffers = mDecoder.GetInputBuffers();

		    int decInputIndex = mDecoder.DequeueInputBuffer(1000000/FRAMERATE);
		    if (decInputIndex>=0) {
			    decInputBuffers[decInputIndex].Clear();
			    decInputBuffers[decInputIndex].Put(prefix);
			    decInputBuffers[decInputIndex].Put(mSPS);
			    mDecoder.QueueInputBuffer(decInputIndex, 0, decInputBuffers[decInputIndex].Position(), timestamp(), 0);
		    } else {
			    if (VERBOSE) Log.e(TAG,"No buffer available !");
		    }

		    decInputIndex = mDecoder.DequeueInputBuffer(1000000/FRAMERATE);
		    if (decInputIndex>=0) {
			    decInputBuffers[decInputIndex].Clear();
			    decInputBuffers[decInputIndex].Put(prefix);
			    decInputBuffers[decInputIndex].Put(mPPS);
			    mDecoder.QueueInputBuffer(decInputIndex, 0, decInputBuffers[decInputIndex].Position(), timestamp(), 0);
		    } else {
			    if (VERBOSE) Log.Error(TAG,"No buffer available !");
		    }


	    }

	    private void releaseDecoder() {
		    if (mDecoder != null) {
			    try {
				    mDecoder.Stop();
			    } catch (Java.Lang.Exception ignore) {}
			    try {
				    mDecoder.Release();
			    } catch (Java.Lang.Exception ignore) {}
		    }
	    }	

	    /**
	     * Tries to obtain the SPS and the PPS for the encoder.
	     */
	    private long searchSPSandPPS() {

		    ByteBuffer[] inputBuffers = mEncoder.GetInputBuffers();
		    ByteBuffer[] outputBuffers = mEncoder.GetOutputBuffers();
		    BufferInfo info = new BufferInfo();
		    byte[] csd = new byte[128];
		    int len = 0, p = 4, q = 4;
		    long elapsed = 0, now = timestamp();

		    while (elapsed<3000000 && (mSPS==null || mPPS==null)) {

			    // Some encoders won't give us the SPS and PPS unless they receive something to encode first...
			    int bufferIndex = mEncoder.DequeueInputBuffer(1000000/FRAMERATE);
			    if (bufferIndex>=0) {
				    check(inputBuffers[bufferIndex].Capacity()>=mData.Length, "The input buffer is not big enough.");
				    inputBuffers[bufferIndex].Clear();
				    inputBuffers[bufferIndex].Put(mData, 0, mData.Length);
				    mEncoder.QueueInputBuffer(bufferIndex, 0, mData.Length, timestamp(), 0);
			    } else {
				    if (VERBOSE) Log.e(TAG,"No buffer available !");
			    }

			    // We are looking for the SPS and the PPS here. As always, Android is very inconsistent, I have observed that some
			    // encoders will give those parameters through the MediaFormat object (that is the normal behaviour).
			    // But some other will not, in that case we try to find a NAL unit of type 7 or 8 in the byte stream outputed by the encoder...

			    int index = mEncoder.DequeueOutputBuffer(info, 1000000/FRAMERATE);

			    if (index == (int)MediaCodecInfoState.OutputFormatChanged) {

                    // The PPS and PPS shoud be there
                    MediaFormat format = mEncoder.OutputFormat;
				    ByteBuffer spsb = format.GetByteBuffer("csd-0");
				    ByteBuffer ppsb = format.GetByteBuffer("csd-1");
				    mSPS = new byte[spsb.Capacity()-4];
				    spsb.Position(4);
				    spsb.Get(mSPS,0,mSPS.Length);
				    mPPS = new byte[ppsb.Capacity()-4];
				    ppsb.Position(4);
				    ppsb.Get(mPPS,0,mPPS.Length);
				    break;

			    } else if (index == (int) MediaCodecInfoState.OutputBuffersChanged) {
                    outputBuffers = mEncoder.GetOutputBuffers();
			    } else if (index>=0) {

				    len = info.Size;
				    if (len<128) {
					    outputBuffers[index].Get(csd,0,len);
					    if (len>0 && csd[0]==0 && csd[1]==0 && csd[2]==0 && csd[3]==1) {
						    // Parses the SPS and PPS, they could be in two different packets and in a different order 
						    //depending on the phone so we don't make any assumption about that
						    while (p<len) {
							    while (!(csd[p+0]==0 && csd[p+1]==0 && csd[p+2]==0 && csd[p+3]==1) && p+3<len) p++;
							    if (p+3>=len) p=len;
							    if ((csd[q]&0x1F)==7) {
								    mSPS = new byte[p-q];
								    JavaSystem.Arraycopy(csd, q, mSPS, 0, p-q);
							    } else {
								    mPPS = new byte[p-q];
                                    JavaSystem.Arraycopy(csd, q, mPPS, 0, p-q);
							    }
							    p += 4;
							    q = p;
						    }
					    }					
				    }
				    mEncoder.ReleaseOutputBuffer(index, false);
			    }

			    elapsed = timestamp() - now;
		    }

		    check(mPPS != null && mSPS != null, "Could not determine the SPS & PPS.");
		    mB64PPS = Base64.EncodeToString(mPPS, 0, mPPS.Length, Base64Flags.NoWrap);
		    mB64SPS = Base64.EncodeToString(mSPS, 0, mSPS.Length, Base64Flags.NoWrap);

		    return elapsed;
	    }

	    private long encode() {
		    int n = 0;
		    long elapsed = 0, now = timestamp();
		    int encOutputIndex = 0, encInputIndex = 0;
		    BufferInfo info = new BufferInfo();
		    ByteBuffer[] encInputBuffers = mEncoder.GetInputBuffers();
		    ByteBuffer[] encOutputBuffers = mEncoder.GetOutputBuffers();

		    while (elapsed<5000000) {
			    // Feeds the encoder with an image
			    encInputIndex = mEncoder.DequeueInputBuffer(1000000/FRAMERATE);
			    if (encInputIndex>=0) {
				    check(encInputBuffers[encInputIndex].Capacity()>=mData.Length, "The input buffer is not big enough.");
				    encInputBuffers[encInputIndex].Clear();
				    encInputBuffers[encInputIndex].Put(mData, 0, mData.Length);
				    mEncoder.QueueInputBuffer(encInputIndex, 0, mData.Length, timestamp(), 0);
			    } else {
				    if (VERBOSE) Log.d(TAG,"No buffer available !");
			    }

			    // Tries to get a NAL unit
			    encOutputIndex = mEncoder.DequeueOutputBuffer(info, 1000000/FRAMERATE);
			    if (encOutputIndex == (int)MediaCodec.InfoOutputBuffersChanged) {
				    encOutputBuffers = mEncoder.GetOutputBuffers();
			    } else if (encOutputIndex>=0) {
				    mVideo[n] = new byte[info.Size];
				    encOutputBuffers[encOutputIndex].Clear();
				    encOutputBuffers[encOutputIndex].Get(mVideo[n++], 0, info.Size);
				    mEncoder.ReleaseOutputBuffer(encOutputIndex, false);
				    if (n>=NB_ENCODED) {
					    flushMediaCodec(mEncoder);
					    return elapsed;
				    }
			    }

			    elapsed = timestamp() - now;
		    }

		    throw new RuntimeException("The encoder is too slow.");

	    }

	    /**
	     * @param withPrefix If set to true, the decoder will be fed with NALs preceeded with 0x00000001.
	     * @return How long it took to decode all the NALs
	     */
	    private long decode(bool withPrefix) {
		    int n = 0, i = 0, j = 0;
		    long elapsed = 0, now = timestamp();
		    int decInputIndex = 0, decOutputIndex = 0;
		    ByteBuffer[] decInputBuffers = mDecoder.GetInputBuffers();
		    ByteBuffer[] decOutputBuffers = mDecoder.GetOutputBuffers();
		    BufferInfo info = new BufferInfo();

		    while (elapsed<3000000) {

			    // Feeds the decoder with a NAL unit
			    if (i<NB_ENCODED) {
				    decInputIndex = mDecoder.DequeueInputBuffer(1000000/FRAMERATE);
				    if (decInputIndex>=0) {
					    int l1 = decInputBuffers[decInputIndex].Capacity();
					    int l2 = mVideo[i].Length;
					    decInputBuffers[decInputIndex].Clear();
					
					    if ((withPrefix && hasPrefix(mVideo[i])) || (!withPrefix && !hasPrefix(mVideo[i]))) {
						    check(l1>=l2, "The decoder input buffer is not big enough (nal="+l2+", capacity="+l1+").");
						    decInputBuffers[decInputIndex].Put(mVideo[i],0,mVideo[i].Length);
					    } else if (withPrefix && !hasPrefix(mVideo[i])) {
						    check(l1>=l2+4, "The decoder input buffer is not big enough (nal="+(l2+4)+", capacity="+l1+").");
						    decInputBuffers[decInputIndex].Put(new byte[] {0,0,0,1});
						    decInputBuffers[decInputIndex].Put(mVideo[i],0,mVideo[i].Length);
					    } else if (!withPrefix && hasPrefix(mVideo[i])) {
						    check(l1>=l2-4, "The decoder input buffer is not big enough (nal="+(l2-4)+", capacity="+l1+").");
						    decInputBuffers[decInputIndex].Put(mVideo[i],4,mVideo[i].Length-4);
					    }
					
					    mDecoder.QueueInputBuffer(decInputIndex, 0, l2, timestamp(), 0);
					    i++;
				    } else {
					    if (VERBOSE) Log.d(TAG,"No buffer available !");
				    }
			    }

			    // Tries to get a decoded image
			    decOutputIndex = mDecoder.DequeueOutputBuffer(info, 1000000/FRAMERATE);
			    if (decOutputIndex == (int)MediaCodec.InfoOutputBuffersChanged) {
				    decOutputBuffers = mDecoder.GetOutputBuffers();
			    } else if (decOutputIndex == (int)MediaCodec.InfoOutputFormatChanged) {
                    mDecOutputFormat = mDecoder.OutputFormat;
			    } else if (decOutputIndex>=0) {
				    if (n>2) {
					    // We have successfully encoded and decoded an image !
					    int length = info.Size;
					    mDecodedVideo[j] = new byte[length];
					    decOutputBuffers[decOutputIndex].Clear();
					    decOutputBuffers[decOutputIndex].Get(mDecodedVideo[j], 0, length);
					    // Converts the decoded frame to NV21
					    convertToNV21(j);
					    if (j>=NB_DECODED-1) {
						    flushMediaCodec(mDecoder);
						    if (VERBOSE) Log.v(TAG, "Decoding "+n+" frames took "+elapsed/1000+" ms");
						    return elapsed;
					    }
					    j++;
				    }
				    mDecoder.ReleaseOutputBuffer(decOutputIndex, false);
				    n++;
			    }	
			    elapsed = timestamp() - now;
		    }

		    throw new RuntimeException("The decoder did not decode anything.");

	    }

	    /**
	     * Makes sure the NAL has a header or not.
	     * @param withPrefix If set to true, the NAL will be preceded with 0x00000001.
	     */
	    private bool hasPrefix(byte[] nal) {
		    return nal[0] == 0 && nal[1] == 0 && nal[2] == 0 && nal[3] == 0x01;
	    }
	
	    /**
	     * @throws IOException The decoder cannot be configured.
	     */
	    private void encodeDecode()  {
		    encode();
		    try {
			    configureDecoder();
			    decode(true);
		    } finally {
			    releaseDecoder();
		    }
	    }

	    private void flushMediaCodec(MediaCodec mc) {
		    int index = 0;
		    BufferInfo info = new BufferInfo();
		    while (index != (int)MediaCodec.InfoTryAgainLater) {
			    index = mc.DequeueOutputBuffer(info, 1000000/FRAMERATE);
			    if (index>=0) {
				    mc.ReleaseOutputBuffer(index, false);
			    }
		    }
	    }

	    private void check(bool cond, System.String message) {
		    if (!cond) {
			    if (VERBOSE) Log.e(TAG,message);
			    throw new IllegalStateException(message);
		    }
	    }

	    private long timestamp() {
		    return JavaSystem.NanoTime()/1000;
	    }

    }
}