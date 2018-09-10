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
using Android.Hardware;
using Android.Media;
using Android.OS;
using Android.Util;
using Android.Views;
using Java.IO;
using Java.Lang;
using System;
using static Android.Hardware.Camera;

namespace Net.Majorkernelpanic.Streaming
{

    /** 
     * Don't use this class directly.
     */
    public abstract class VideoStream : MediaStream , ISurfaceHolderCallback,IPreviewCallback
    {

	    protected new const System.String TAG = "VideoStream";

	    protected VideoQuality mRequestedQuality = VideoQuality.DEFAULT_VIDEO_QUALITY.clone();
	    protected VideoQuality mQuality = VideoQuality.DEFAULT_VIDEO_QUALITY.clone();
        protected SurfaceView mSurfaceView = null;
	    protected ISharedPreferences mSettings = null;
	    protected int mVideoEncoder, mCameraId = 0;
	    protected int mRequestedOrientation = 0, mOrientation = 0;
	    protected Android.Hardware.Camera mCamera;
	    protected Thread mCameraThread;
	    protected Looper mCameraLooper;

	    protected bool mCameraOpenedManually = true;
	    protected bool mFlashEnabled = false;
	    protected bool mSurfaceReady = false;
	    protected bool mUnlocked = false;
	    protected bool mPreviewStarted = false;
	    protected bool mUpdated = false;
	
	    protected System.String mMimeType;
	    protected System.String mEncoderName;
	    protected int mEncoderColorFormat;
	    protected int mCameraImageFormat;
	    protected int mMaxFps = 0;


        private ISurfaceHolderCallback mObjectCallBack = null;

     //   private MediaCodec mMediaCodec = null;



        /** 
	     * Don't use this class directly.
	     * Uses CAMERA_FACING_BACK by default.
	     */
        public VideoStream() {
            setCamera((int)Android.Hardware.CameraFacing.Back);
        }	

	    /** 
	     * Don't use this class directly
	     * @param camera Can be either CameraInfo.CAMERA_FACING_BACK or CameraInfo.CAMERA_FACING_FRONT
	     */
	    
	    public VideoStream(int camera) {
		    setCamera(camera);
	    }

	    /**
	     * Sets the camera that will be used to capture video.
	     * You can call this method at any time and changes will take effect next time you start the stream.
	     * @param camera Can be either CameraInfo.CAMERA_FACING_BACK or CameraInfo.CAMERA_FACING_FRONT
	     */
	    public void setCamera(int camera) {
		    CameraInfo cameraInfo = new CameraInfo();
		    int numberOfCameras = Android.Hardware.Camera.NumberOfCameras;
		    for (int i=0;i<numberOfCameras;i++) {
                Android.Hardware.Camera.GetCameraInfo(i, cameraInfo);
			    if ((int)cameraInfo.Facing == camera) {
				    mCameraId = i;
				    break;
			    }
		    }
	    }

	    /**	Switch between the front facing and the back facing camera of the phone. 
	     * If {@link #startPreview()} has been called, the preview will be  briefly interrupted. 
	     * If {@link #start()} has been called, the stream will be  briefly interrupted.
	     * You should not call this method from the main thread if you are already streaming. 
	     * @throws IOException 
	     * @throws RuntimeException 
	     **/
	    public void switchCamera() {
		    if (Camera.NumberOfCameras == 1) throw new IllegalStateException("Phone only has one camera !");
		    bool streaming = mStreaming;
		    bool previewing = mCamera!=null && mCameraOpenedManually; 
		    mCameraId = (mCameraId == (int)CameraInfo.CameraFacingBack) ? (int)CameraInfo.CameraFacingFront : (int)CameraInfo.CameraFacingBack; 
		    setCamera(mCameraId);
		    stopPreview();
		    mFlashEnabled = false;
		    if (previewing) startPreview();
		    if (streaming) start(); 
	    }

	    /**
	     * Returns the id of the camera currently selected. 
	     * Can be either {@link CameraInfo#CAMERA_FACING_BACK} or 
	     * {@link CameraInfo#CAMERA_FACING_FRONT}.
	     */
	    public int getCamera() {
		    return mCameraId;
	    }

        public void surfaceDestroyed(ISurfaceHolder holder)
        {
            mSurfaceReady = false;
            stopPreview();
            Log.Debug(TAG, "Surface destroyed !");
        }
        public void surfaceCreated(ISurfaceHolder holder)
        {
            mSurfaceReady = true;
        }

        public void surfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
            Log.Debug(TAG, "Surface Changed !");
        }
        /**
	     * Sets a Surface to show a preview of recorded media (video). 
	     * You can call this method at any time and changes will take effect next time you call {@link #start()}.
	     */
        public void setSurfaceView(SurfaceView view)
        {
            lock(this)
            {
                mSurfaceView = view;
                if (mObjectCallBack != null && mSurfaceView != null && mSurfaceView.Holder != null) {
                    mSurfaceView.Holder.RemoveCallback(this);
                }
                if (mSurfaceView != null && mSurfaceView.Holder != null)
                    mObjectCallBack = this;
                
                mSurfaceView.Holder.AddCallback(mObjectCallBack);
                mSurfaceReady = true;
            }
            
	    }

	    /** Turns the LED on or off if phone has one. */
	    public void setFlashState(bool state)
        {
            lock (this)
            {
                // If the camera has already been opened, we apply the change immediately
                if (mCamera != null)
                {

                    if (mStreaming && mMode == MODE_MEDIARECORDER_API)
                    {
                        lockCamera();
                    }

                    Parameters parameters = mCamera.GetParameters();

                    // We test if the phone has a flash
                    if (parameters.FlashMode == null)
                    {
                        // The phone has no flash or the choosen camera can not toggle the flash
                        throw new RuntimeException("Can't turn the flash on !");
                    }
                    else
                    {
                        parameters.FlashMode = state ? Parameters.FlashModeTorch : Parameters.FlashModeOff);
                        try
                        {
                            mCamera.SetParameters(parameters);
                            mFlashEnabled = state;
                        }
                        catch (RuntimeException e)
                        {
                            mFlashEnabled = false;
                            throw new RuntimeException("Can't turn the flash on !");
                        }
                        finally
                        {
                            if (mStreaming && mMode == MODE_MEDIARECORDER_API)
                            {
                                unlockCamera();
                            }
                        }
                    }
                }
                else
                {
                    mFlashEnabled = state;
                }
            }
	    }

	    /** 
	     * Toggles the LED of the phone if it has one.
	     * You can get the current state of the flash with {@link VideoStream#getFlashState()}.
	     */
	    public void toggleFlash() {
            lock(this)
            { 
		        setFlashState(!mFlashEnabled);
            }
        }

	    /** Indicates whether or not the flash of the phone is on. */
	    public bool getFlashState() {
		    return mFlashEnabled;
	    }

	    /** 
	     * Sets the orientation of the preview.
	     * @param orientation The orientation of the preview
	     */
	    public void setPreviewOrientation(int orientation) {
		    mRequestedOrientation = orientation;
		    mUpdated = false;
	    }
	
	    /** 
	     * Sets the configuration of the stream. You can call this method at any time 
	     * and changes will take effect next time you call {@link #configure()}.
	     * @param videoQuality Quality of the stream
	     */
	    public void setVideoQuality(VideoQuality videoQuality) {
		    if (!mRequestedQuality.equals(videoQuality)) {
			    mRequestedQuality = videoQuality.clone();
			    mUpdated = false;
		    }
	    }

	    /** 
	     * Returns the quality of the stream.  
	     */
	    public VideoQuality getVideoQuality() {
		    return mRequestedQuality;
	    }

	    /**
	     * Some data (SPS and PPS params) needs to be stored when {@link #getSessionDescription()} is called 
	     * @param prefs The SharedPreferences that will be used to save SPS and PPS parameters
	     */
	    public void setPreferences(ISharedPreferences prefs) {
		    mSettings = prefs;
	    }

	    /**
	     * Configures the stream. You need to call this before calling {@link #getSessionDescription()} 
	     * to apply your configuration of the stream.
	     */
	    public void configure() {
            lock(this)
            { 
		        base.configure();
		        mOrientation = mRequestedOrientation;
            }
        }	
	
	    /**
	     * Starts the stream.
	     * This will also open the camera and display the preview 
	     * if {@link #startPreview()} has not already been called.
	     */
	    public void start()
        {
		    if (!mPreviewStarted)
                mCameraOpenedManually = false;
		    base.start();
		    Log.Debug(TAG,"Stream configuration: FPS: "+mQuality.framerate+" Width: "+mQuality.resX+" Height: "+mQuality.resY);
	    }

	    /** Stops the stream. */
	    public void stop()
        {
            lock (this)
            {
                if (mCamera != null)
                {
                    if (mMode == MODE_MEDIACODEC_API)
                    {
                        mCamera.SetPreviewCallbackWithBuffer(null);
                    }
                    if (mMode == MODE_MEDIACODEC_API_2)
                    {
                        ((SurfaceView)mSurfaceView).removeMediaCodecSurface();
                    }
                    base.stop();
                    // We need to restart the preview
                    if (!mCameraOpenedManually)
                    {
                        destroyCamera();
                    }
                    else
                    {
                        try
                        {
                            startPreview();
                        }
                        catch (RuntimeException e)
                        {
                            e.PrintStackTrace();
                        }
                    }
                }
            }
	    }

	    public  void startPreview() 
        {
            lock(this)
            { 
		        mCameraOpenedManually = true;
		        if (!mPreviewStarted) {
			        createCamera();
			        updateCamera();
		        }
            }
        }

	    /**
	     * Stops the preview.
	     */
	    public void stopPreview() {
            lock (this)
            {
                mCameraOpenedManually = false;
                stop();
            }
	    }

	    /**
	     * Video encoding is done by a MediaRecorder.
	     */
	    protected override void encodeWithMediaRecorder()
        {

		    Log.Debug(TAG,"Video encoded using the MediaRecorder API");

		    // We need a local socket to forward data output by the camera to the packetizer
		    createSockets();

		    // Reopens the camera if needed
		    destroyCamera();
		    createCamera();

		    // The camera must be unlocked before the MediaRecorder can use it
		    unlockCamera();

		    try {
			    mMediaRecorder = new MediaRecorder();
			    mMediaRecorder.SetCamera(mCamera);
			    mMediaRecorder.SetVideoSource(VideoSource.Camera);
			    mMediaRecorder.SetOutputFormat(OutputFormat.ThreeGpp);
			    mMediaRecorder.SetVideoEncoder((VideoEncoder) mVideoEncoder);
			    mMediaRecorder.SetPreviewDisplay(mSurfaceView.Holder.Surface);
			    mMediaRecorder.SetVideoSize(mRequestedQuality.resX,mRequestedQuality.resY);
			    mMediaRecorder.SetVideoFrameRate(mRequestedQuality.framerate);

			    // The bandwidth actually consumed is often above what was requested 
			    mMediaRecorder.SetVideoEncodingBitRate((int)(mRequestedQuality.bitrate*0.8));

			    // We write the output of the camera in a local socket instead of a file !			
			    // This one little trick makes streaming feasible quiet simply: data from the camera
			    // can then be manipulated at the other end of the socket
			    FileDescriptor fd = null;
			    if (sPipeApi == PIPE_API_PFD) {
				    fd = mParcelWrite.FileDescriptor;
			    } else  {
				    fd = mSender.FileDescriptor;
			    }
			    mMediaRecorder.SetOutputFile(fd);

			    mMediaRecorder.Prepare();
			    mMediaRecorder.Start();

		    } catch (System.Exception e) {
			    throw new ConfNotSupportedException(e.Message);
		    }

		    InputStream inputStream = null;
            System.IO.Stream inputSStream = null;

		    if (sPipeApi == PIPE_API_PFD) {
                inputStream = new ParcelFileDescriptor.AutoCloseInputStream(mParcelRead);
		    } else  {
                inputSStream = mReceiver.InputStream;
		    }

		    // This will skip the MPEG4 header if this step fails we can't stream anything :(
		    try {
			    byte[] buffer = new byte[4];
			    // Skip all atoms preceding mdat atom
			    while (!Thread.Interrupted())
                {
                    if (inputSStream != null)
                    {
                        while (inputSStream.ReadByte() != 'm') ;
                        inputSStream.Read(buffer, 0, 3);
                    }
                    else
                    { 
				        while (inputStream.Read() != 'm');
                        inputStream.Read(buffer,0,3);
                    }

                    if (buffer[0] == 'd' && buffer[1] == 'a' && buffer[2] == 't') break;
			    }
		    } catch (IOException e) {
			    Log.Error(TAG,"Couldn't skip mp4 header :/");
			    stop();
			    throw e;
		    }

		    // The packetizer encapsulates the bit stream in an RTP stream and send it over the network
		    mPacketizer.setInputStream(inputStream);
            mPacketizer.setInputSStream(inputSStream);

            mPacketizer.start();

		    mStreaming = true;

	    }


	    /**
	     * Video encoding is done by a MediaCodec.
	     */
	    protected override void encodeWithMediaCodec() {
		    if (mMode == MODE_MEDIACODEC_API_2) {
			    // Uses the method MediaCodec.createInputSurface to feed the encoder
			    encodeWithMediaCodecMethod2();
		    } else {
			    // Uses dequeueInputBuffer to feed the encoder
			    encodeWithMediaCodecMethod1();
		    }
	    }

        Camera.PreviewCallback callback = new Camera.PreviewCallback() {
                long now = System.nanoTime()/1000, oldnow = now, i=0;
        ByteBuffer[] inputBuffers = mMediaCodec.getInputBuffers();
        @Override
        public void onPreviewFrame(byte[] data, Camera camera)
        {
            oldnow = now;
            now = System.nanoTime() / 1000;
            if (i++ > 3)
            {
                i = 0;
                //Log.d(TAG,"Measured: "+1000000L/(now-oldnow)+" fps.");
            }
            try
            {
                int bufferIndex = mMediaCodec.dequeueInputBuffer(500000);
                if (bufferIndex >= 0)
                {
                    inputBuffers[bufferIndex].clear();
                    if (data == null) Log.e(TAG, "Symptom of the \"Callback buffer was to small\" problem...");
                    else convertor.convert(data, inputBuffers[bufferIndex]);
                    mMediaCodec.queueInputBuffer(bufferIndex, 0, inputBuffers[bufferIndex].position(), now, 0);
                }
                else
                {
                    Log.e(TAG, "No buffer available !");
                }
            }
            finally
            {
                mCamera.addCallbackBuffer(data);
            }
        }
    

    /**
     * Video encoding is done by a MediaCodec.
     */

    protected void encodeWithMediaCodecMethod1() {

		    Log.Debug(TAG,"Video encoded using the MediaCodec API with a buffer");

		    // Updates the parameters of the camera if needed
		    createCamera();
		    updateCamera();

		    // Estimates the frame rate of the camera
		    measureFramerate();

		    // Starts the preview if needed
		    if (!mPreviewStarted) {
			    try {
				    mCamera.startPreview();
				    mPreviewStarted = true;
			    } catch (RuntimeException e) {
				    destroyCamera();
				    throw e;
			    }
		    }

		    EncoderDebugger debugger = EncoderDebugger.debug(mSettings, mQuality.resX, mQuality.resY);
		    const NV21Convertor convertor = debugger.getNV21Convertor();

		    mMediaCodec = MediaCodec.CreateByCodecName(debugger.getEncoderName());
		    MediaFormat mediaFormat = MediaFormat.CreateVideoFormat("video/avc", mQuality.resX, mQuality.resY);
		    mediaFormat.SetInteger(MediaFormat.KeyBitRate, mQuality.bitrate);
		    mediaFormat.SetInteger(MediaFormat.KeyFrameRate, mQuality.framerate);	
		    mediaFormat.SetInteger(MediaFormat.KeyColorFormat,debugger.getEncoderColorFormat());
		    mediaFormat.SetInteger(MediaFormat.KeyIFrameInterval, 1);
		    mMediaCodec.Configure(mediaFormat, null, null, MediaCodecConfigFlags.Encode);
		    mMediaCodec.Start();

		    Camera.PreviewCallback callback = new Camera.PreviewCallback() Action InnerMethod = () =>{
			    long now = System.nanoTime()/1000, oldnow = now, i=0;
			    ByteBuffer[] inputBuffers = mMediaCodec.getInputBuffers();
			    @Override
			    public void onPreviewFrame(byte[] data, Camera camera) {
				    oldnow = now;
				    now = System.nanoTime()/1000;
				    if (i++>3) {
					    i = 0;
					    //Log.d(TAG,"Measured: "+1000000L/(now-oldnow)+" fps.");
				    }
				    try {
					    int bufferIndex = mMediaCodec.dequeueInputBuffer(500000);
					    if (bufferIndex>=0) {
						    inputBuffers[bufferIndex].clear();
						    if (data == null) Log.e(TAG,"Symptom of the \"Callback buffer was to small\" problem...");
						    else convertor.convert(data, inputBuffers[bufferIndex]);
						    mMediaCodec.queueInputBuffer(bufferIndex, 0, inputBuffers[bufferIndex].position(), now, 0);
					    } else {
						    Log.e(TAG,"No buffer available !");
					    }
				    } finally {
					    mCamera.addCallbackBuffer(data);
				    }				
			    }
		    };
		
		    for (int i=0;i<10;i++) mCamera.addCallbackBuffer(new byte[convertor.getBufferSize()]);
		    mCamera.setPreviewCallbackWithBuffer(callback);

		    // The packetizer encapsulates the bit stream in an RTP stream and send it over the network
		    mPacketizer.setInputStream(new MediaCodecInputStream(mMediaCodec));
		    mPacketizer.start();

		    mStreaming = true;

	    }

	    /**
	     * Video encoding is done by a MediaCodec.
	     * But here we will use the buffer-to-surface method
	     */
	    @SuppressLint({ "InlinedApi", "NewApi" })	
	    protected void encodeWithMediaCodecMethod2() throws RuntimeException, IOException {

		    Log.d(TAG,"Video encoded using the MediaCodec API with a surface");

		    // Updates the parameters of the camera if needed
		    createCamera();
		    updateCamera();

		    // Estimates the frame rate of the camera
		    measureFramerate();

		    EncoderDebugger debugger = EncoderDebugger.debug(mSettings, mQuality.resX, mQuality.resY);

		    mMediaCodec = MediaCodec.createByCodecName(debugger.getEncoderName());
		    MediaFormat mediaFormat = MediaFormat.createVideoFormat("video/avc", mQuality.resX, mQuality.resY);
		    mediaFormat.setInteger(MediaFormat.KEY_BIT_RATE, mQuality.bitrate);
		    mediaFormat.setInteger(MediaFormat.KEY_FRAME_RATE, mQuality.framerate);	
		    mediaFormat.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
		    mediaFormat.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
		    mMediaCodec.configure(mediaFormat, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
		    Surface surface = mMediaCodec.createInputSurface();
		    ((SurfaceView)mSurfaceView).addMediaCodecSurface(surface);
		    mMediaCodec.start();

		    // The packetizer encapsulates the bit stream in an RTP stream and send it over the network
		    mPacketizer.setInputStream(new MediaCodecInputStream(mMediaCodec));
		    mPacketizer.start();

		    mStreaming = true;

	    }

	    /**
	     * Returns a description of the stream using SDP. 
	     * This method can only be called after {@link Stream#configure()}.
	     * @throws IllegalStateException Thrown when {@link Stream#configure()} wa not called.
	     */	
	    public abstract String getSessionDescription() throws IllegalStateException;

	    /**
	     * Opens the camera in a new Looper thread so that the preview callback is not called from the main thread
	     * If an exception is thrown in this Looper thread, we bring it back into the main thread.
	     * @throws RuntimeException Might happen if another app is already using the camera.
	     */
	    private void openCamera() throws RuntimeException {
		    final Semaphore lock = new Semaphore(0);
		    final RuntimeException[] exception = new RuntimeException[1];
		    mCameraThread = new Thread(new Runnable() {
			    @Override
			    public void run() {
				    Looper.prepare();
				    mCameraLooper = Looper.myLooper();
				    try {
					    mCamera = Camera.open(mCameraId);
				    } catch (RuntimeException e) {
					    exception[0] = e;
				    } finally {
					    lock.release();
					    Looper.loop();
				    }
			    }
		    });
		    mCameraThread.start();
		    lock.acquireUninterruptibly();
		    if (exception[0] != null) throw new CameraInUseException(exception[0].getMessage());
	    }

	    protected synchronized void createCamera() throws RuntimeException {
		    if (mSurfaceView == null)
			    throw new InvalidSurfaceException("Invalid surface !");
		    if (mSurfaceView.getHolder() == null || !mSurfaceReady) 
			    throw new InvalidSurfaceException("Invalid surface !");

		    if (mCamera == null) {
			    openCamera();
			    mUpdated = false;
			    mUnlocked = false;
			    mCamera.setErrorCallback(new Camera.ErrorCallback() {
				    @Override
				    public void onError(int error, Camera camera) {
					    // On some phones when trying to use the camera facing front the media server will die
					    // Whether or not this callback may be called really depends on the phone
					    if (error == Camera.CAMERA_ERROR_SERVER_DIED) {
						    // In this case the application must release the camera and instantiate a new one
						    Log.e(TAG,"Media server died !");
						    // We don't know in what thread we are so stop needs to be synchronized
						    mCameraOpenedManually = false;
						    stop();
					    } else {
						    Log.e(TAG,"Error unknown with the camera: "+error);
					    }	
				    }
			    });

			    try {

				    // If the phone has a flash, we turn it on/off according to mFlashEnabled
				    // setRecordingHint(true) is a very nice optimization if you plane to only use the Camera for recording
				    Parameters parameters = mCamera.getParameters();
				    if (parameters.getFlashMode()!=null) {
					    parameters.setFlashMode(mFlashEnabled?Parameters.FLASH_MODE_TORCH:Parameters.FLASH_MODE_OFF);
				    }
				    parameters.setRecordingHint(true);
				    mCamera.setParameters(parameters);
				    mCamera.setDisplayOrientation(mOrientation);

				    try {
					    if (mMode == MODE_MEDIACODEC_API_2) {
						    mSurfaceView.startGLThread();
						    mCamera.setPreviewTexture(mSurfaceView.getSurfaceTexture());
					    } else {
						    mCamera.setPreviewDisplay(mSurfaceView.getHolder());
					    }
				    } catch (IOException e) {
					    throw new InvalidSurfaceException("Invalid surface !");
				    }

			    } catch (RuntimeException e) {
				    destroyCamera();
				    throw e;
			    }

		    }
	    }

	    protected synchronized void destroyCamera() {
		    if (mCamera != null) {
			    if (mStreaming) super.stop();
			    lockCamera();
			    mCamera.stopPreview();
			    try {
				    mCamera.release();
			    } catch (Exception e) {
				    Log.e(TAG,e.getMessage()!=null?e.getMessage():"unknown error");
			    }
			    mCamera = null;
			    mCameraLooper.quit();
			    mUnlocked = false;
			    mPreviewStarted = false;
		    }	
	    }

	    protected synchronized void updateCamera() throws RuntimeException {
		
		    // The camera is already correctly configured
		    if (mUpdated) return;
		
		    if (mPreviewStarted) {
			    mPreviewStarted = false;
			    mCamera.stopPreview();
		    }

		    Parameters parameters = mCamera.getParameters();
		    mQuality = VideoQuality.determineClosestSupportedResolution(parameters, mQuality);
		    int[] max = VideoQuality.determineMaximumSupportedFramerate(parameters);
		
		    double ratio = (double)mQuality.resX/(double)mQuality.resY;
		    mSurfaceView.requestAspectRatio(ratio);
		
		    parameters.setPreviewFormat(mCameraImageFormat);
		    parameters.setPreviewSize(mQuality.resX, mQuality.resY);
		    parameters.setPreviewFpsRange(max[0], max[1]);

		    try {
			    mCamera.setParameters(parameters);
			    mCamera.setDisplayOrientation(mOrientation);
			    mCamera.startPreview();
			    mPreviewStarted = true;
			    mUpdated = true;
		    } catch (RuntimeException e) {
			    destroyCamera();
			    throw e;
		    }
	    }

	    protected void lockCamera() {
		    if (mUnlocked) {
			    Log.d(TAG,"Locking camera");
			    try {
				    mCamera.reconnect();
			    } catch (Exception e) {
				    Log.e(TAG,e.getMessage());
			    }
			    mUnlocked = false;
		    }
	    }

	    protected void unlockCamera() {
		    if (!mUnlocked) {
			    Log.d(TAG,"Unlocking camera");
			    try {	
				    mCamera.unlock();
			    } catch (Exception e) {
				    Log.e(TAG,e.getMessage());
			    }
			    mUnlocked = true;
		    }
	    }


	    /**
	     * Computes the average frame rate at which the preview callback is called.
	     * We will then use this average frame rate with the MediaCodec.  
	     * Blocks the thread in which this function is called.
	     */
	    private void measureFramerate() {
		    final Semaphore lock = new Semaphore(0);

		    final Camera.PreviewCallback callback = new Camera.PreviewCallback() {
			    int i = 0, t = 0;
			    long now, oldnow, count = 0;
			    @Override
			    public void onPreviewFrame(byte[] data, Camera camera) {
				    i++;
				    now = System.nanoTime()/1000;
				    if (i>3) {
					    t += now - oldnow;
					    count++;
				    }
				    if (i>20) {
					    mQuality.framerate = (int) (1000000/(t/count)+1);
					    lock.release();
				    }
				    oldnow = now;
			    }
		    };

		    mCamera.setPreviewCallback(callback);

		    try {
			    lock.tryAcquire(2,TimeUnit.SECONDS);
			    Log.d(TAG,"Actual framerate: "+mQuality.framerate);
			    if (mSettings != null) {
				    Editor editor = mSettings.edit();
				    editor.putInt(PREF_PREFIX+"fps"+mRequestedQuality.framerate+","+mCameraImageFormat+","+mRequestedQuality.resX+mRequestedQuality.resY, mQuality.framerate);
				    editor.commit();
			    }
		    } catch (InterruptedException e) {}

		    mCamera.setPreviewCallback(null);

	    }	

    }

}