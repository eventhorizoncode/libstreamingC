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
 * A class for streaming H.264 from the camera of an android device using RTP.
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
using Java.Util.Concurrent;
using System;
using System.Collections;
using System.Collections.Generic;

using static Android.Media.MediaCodec;

namespace Net.Majorkernelpanic.Streaming
{
    public class H264Stream : VideoStream , MediaRecorder.IOnInfoListener
    {

	    public const System.String TAG = "H264Stream";

	    private Semaphore mLock = new Semaphore(0);
	    private MP4Config mConfig;

        private void contructStream(int cam) 
        {
            mMimeType = "video/avc";
            mCameraImageFormat = (int) ImageFormatType.Nv21;
            mVideoEncoder =(int) VideoEncoder.H264;
            mPacketizer = new H264Packetizer();
        }
	    /**
	     * Constructs the H.264 stream.
	     * Uses CAMERA_FACING_BACK by default.
	     */
	    public H264Stream() {
            contructStream((int)Android.Hardware.CameraFacing.Back);
	    }

	    /**
	     * Constructs the H.264 stream.
	     * @param cameraId Can be either CameraInfo.CAMERA_FACING_BACK or CameraInfo.CAMERA_FACING_FRONT
	     * @throws IOException
	     */
	    public H264Stream(int cameraId) : base(cameraId)
        {
            contructStream(cameraId);

        }

	    /**
	     * Returns a description of the stream using SDP. It can then be included in an SDP file.
	     */
	    public System.String getSessionDescription() {

            lock(this)
            { 
		        if (mConfig == null)
                        throw new IllegalStateException("You need to call configure() first !");

		        return "m=video "+ getDestinationPorts()[0].ToString()+" RTP/AVP 96\r\n" +
		        "a=rtpmap:96 H264/90000\r\n" +
		        "a=fmtp:96 packetization-mode=1;profile-level-id="+mConfig.getProfileLevel()+";sprop-parameter-sets="+mConfig.getB64SPS()+","+mConfig.getB64PPS()+";\r\n";
            }
        }	

	    /**
	     * Starts the stream.
	     * This will also open the camera and display the preview if {@link #startPreview()} has not already been called.
	     */
	    public void start() {
            lock(this)
            { 
		        if (!mStreaming) {
			        configure();
			        byte[] pps = Base64.Decode(mConfig.getB64PPS(), Base64Flags.NoWrap);
			        byte[] sps = Base64.Decode(mConfig.getB64SPS(), Base64Flags.NoWrap);
			        ((H264Packetizer)mPacketizer).setStreamParameters(pps, sps);
			        base.start();
		        }
            }
        }

	    /**
	     * Configures the stream. You need to call this before calling {@link #getSessionDescription()} to apply
	     * your configuration of the stream.
	     */
	    public  void configure() {
            lock(this)
            { 
                base.configure();
		        mMode = mRequestedMode;
		        mQuality = mRequestedQuality.clone();
		        mConfig = testH264();
            }
        }
	
	    /** 
	     * Tests if streaming with the given configuration (bit rate, frame rate, resolution) is possible 
	     * and determines the pps and sps. Should not be called by the UI thread.
	     **/
	    private MP4Config testH264() {
		    if (mMode != MODE_MEDIARECORDER_API) return testMediaCodecAPI();
		    else return testMediaRecorderAPI();
	    }

	    
	    private MP4Config testMediaCodecAPI() {
		    createCamera();
		    updateCamera();
		    try {
			    if (mQuality.resX>=640) {
				    // Using the MediaCodec API with the buffer method for high resolutions is too slow
				    mMode = MODE_MEDIARECORDER_API;
			    }
			    EncoderDebugger debugger = EncoderDebugger.debug(mSettings, mQuality.resX, mQuality.resY);
			    return new MP4Config(debugger.getB64SPS(), debugger.getB64PPS());
		    } catch (Java.Lang.Exception e) {
			    // Fallback on the old streaming method using the MediaRecorder API
			    Log.e(TAG,"Resolution not supported with the MediaCodec API, we fallback on the old streamign method.");
			    mMode = MODE_MEDIARECORDER_API;
			    return testH264();
		    }
	    }

        public void onInfo(MediaRecorder mr, MediaRecorderInfo what, int extra)
        {
            Log.Debug(TAG, "MediaRecorder callback called !");
            if (what == MediaRecorderInfo.MaxDurationReached)
            {
                Log.Debug(TAG, "MediaRecorder: MAX_DURATION_REACHED");
            }
            else if (what == MediaRecorderInfo.MaxFilesizeReached)
            {
                Log.Debug(TAG, "MediaRecorder: MAX_FILESIZE_REACHED");
            }
            else if (what == MediaRecorderInfo.Unknown)
            {
                Log.Debug(TAG, "MediaRecorder: INFO_UNKNOWN");
            }
            else
            {
                Log.Debug(TAG, "WTF ?");
            }
            mLock.Release();
        }
        // Should not be called by the UI thread
        private MP4Config testMediaRecorderAPI() {

		        System.String key = PREF_PREFIX+"h264-mr-"+mRequestedQuality.framerate+","+mRequestedQuality.resX+","+mRequestedQuality.resY;
	
		        if (mSettings != null && mSettings.Contains(key) ) {
			        System.String[] s = mSettings.GetString(key, "").Split(',');
			        return new MP4Config(s[0],s[1],s[2]);
		        }
		
		        if (!Android.OS.Environment.ExternalStorageState.Equals(Android.OS.Environment.MediaMounted)) {
			        throw new StorageUnavailableException("No external storage or external storage not ready !");
		        }

		        System.String TESTFILE = Android.OS.Environment.ExternalStorageDirectory.ToString()+"/spydroid-test.mp4";
		
		        Log.Info(TAG,"Testing H264 support... Test file saved at: "+TESTFILE);

		        try {
			        File file = new File(TESTFILE);
			        file.CreateNewFile();
		        } catch (IOException e) {
			        throw new StorageUnavailableException(e.Message);
		        }
		
		        // Save flash state & set it to false so that led remains off while testing h264
		        bool savedFlashState = mFlashEnabled;
		        mFlashEnabled = false;

		        bool previewStarted = mPreviewStarted;
		
		        bool cameraOpen = mCamera!=null;
		        createCamera();

		        // Stops the preview if needed
		        if (mPreviewStarted) {
			        lockCamera();
			        try {
				        mCamera.stopPreview();
			        } catch (Exception e) {}
			        mPreviewStarted = false;
		        }

		        try {
			        Thread.Sleep(100);
		        } catch (InterruptedException e1) {
			        // TODO Auto-generated catch block
			        e1.PrintStackTrace();
		        }

		        unlockCamera();

		        try {
			
			        mMediaRecorder = new MediaRecorder();
			        mMediaRecorder.SetCamera(mCamera);
			        mMediaRecorder.SetVideoSource(VideoSource.Camera);
			        mMediaRecorder.SetOutputFormat(OutputFormat.ThreeGpp);
			        mMediaRecorder.SetVideoEncoder((VideoEncoder)mVideoEncoder);
			        mMediaRecorder.SetPreviewDisplay(mSurfaceView.Holder.Surface);
			        mMediaRecorder.SetVideoSize(mRequestedQuality.resX,mRequestedQuality.resY);
			        mMediaRecorder.SetVideoFrameRate(mRequestedQuality.framerate);
			        mMediaRecorder.SetVideoEncodingBitRate((int)(mRequestedQuality.bitrate*0.8));
			        mMediaRecorder.SetOutputFile(TESTFILE);
			        mMediaRecorder.SetMaxDuration(3000);

                // We wait a little and stop recording
                    mMediaRecorder.SetOnInfoListener(this);

                    /*mMediaRecorder.SetOnInfoListener(new MediaRecorder.IOnInfoListener() {
				        public void onInfo(MediaRecorder mr, int what, int extra) {
					        Log.d(TAG,"MediaRecorder callback called !");
					        if (what==MediaRecorder.MEDIA_RECORDER_INFO_MAX_DURATION_REACHED) {
						        Log.d(TAG,"MediaRecorder: MAX_DURATION_REACHED");
					        } else if (what==MediaRecorder.MEDIA_RECORDER_INFO_MAX_FILESIZE_REACHED) {
						        Log.d(TAG,"MediaRecorder: MAX_FILESIZE_REACHED");
					        } else if (what==MediaRecorder.MEDIA_RECORDER_INFO_UNKNOWN) {
						        Log.d(TAG,"MediaRecorder: INFO_UNKNOWN");
					        } else {
						        Log.d(TAG,"WTF ?");
					        }
					        mLock.release();
				        }
			        });*/

                // Start recording
                mMediaRecorder.Prepare();
			    mMediaRecorder.Start();

			        if (mLock.TryAcquire(6,TimeUnit.Seconds)) {
				        Log.d(TAG,"MediaRecorder callback was called :)");
				        Thread.Sleep(400);
			        } else {
				        Log.d(TAG,"MediaRecorder callback was not called after 6 seconds... :(");
			        }
		        } catch (IOException e) {
			        throw new ConfNotSupportedException(e.Message);
		        } catch (RuntimeException e) {
			        throw new ConfNotSupportedException(e.Message);
		        } catch (InterruptedException e) {
			        e.PrintStackTrace();
		        } finally {
			        try {
				        mMediaRecorder.stop();
			        } catch (Java.Lang.Exception e) {}
			        mMediaRecorder.Release();
			        mMediaRecorder = null;
			        lockCamera();
			        if (!cameraOpen) destroyCamera();
			        // Restore flash state
			        mFlashEnabled = savedFlashState;
			        if (previewStarted) {
				        // If the preview was started before the test, we try to restart it.
				        try {
					        startPreview();
				        } catch (Java.Lang.Exception e) {}
			        }
		        }

		        // Retrieve SPS & PPS & ProfileId with MP4Config
		        MP4Config config = new MP4Config(TESTFILE);

		        // Delete dummy video
		        File file = new File(TESTFILE);
		        if (!file.Delete()) Log.e(TAG,"Temp file could not be erased");

		        Log.i(TAG,"H264 Test succeded...");

		        // Save test result
		        if (mSettings != null) {
			        ISharedPreferencesEditor editor = mSettings.Edit();
			        editor.PutString(key, config.getProfileLevel()+","+config.getB64SPS()+","+config.getB64PPS());
			        editor.Commit();
		        }

		        return config;

	        }
	
        }


}