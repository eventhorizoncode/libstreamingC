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
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Java.Lang;
using Java.Util.Concurrent;
using System;
/**
* An enhanced SurfaceView in which the camera preview will be rendered. 
* This class was needed for two reasons. <br /> 
* 
* First, it allows to use to feed MediaCodec with the camera preview 
* using the surface-to-buffer method while rendering it in a surface 
* visible to the user. To force the surface-to-buffer method in 
* libstreaming, call {@link MediaStream#setStreamingMethod(byte)}
* with {@link MediaStream#MODE_MEDIACODEC_API_2}. <br /> 
* 
* Second, it allows to force the aspect ratio of the SurfaceView 
* to match the aspect ratio of the camera preview, so that the 
* preview do not appear distorted to the user of your app. To do 
* that, call {@link SurfaceView#setAspectRatioMode(int)} with
* {@link SurfaceView#ASPECT_RATIO_PREVIEW} after creating your 
* {@link SurfaceView}. <br />
* 
*/
namespace Net.Majorkernelpanic.Streaming
{

    public class SurfaceView : Android.Views.SurfaceView, SurfaceTexture.IOnFrameAvailableListener, ISurfaceHolderCallback
    {

	public const System.String TAG = "SurfaceView";

	/** 
	 * The aspect ratio of the surface view will be equal 
	 * to the aspect ration of the camera preview.
	 **/
	public const int ASPECT_RATIO_PREVIEW = 0x01;
	
	/** The surface view will fill completely fill its parent. */
	public  const int ASPECT_RATIO_STRETCH = 0x00;
	
	private Thread mThread = null;
	private Handler mHandler = null;
	private bool mFrameAvailable = false; 
	private bool mRunning = true;
	private int mAspectRatioMode = ASPECT_RATIO_STRETCH;

	// The surface in which the preview is rendered
	private SurfaceManager mViewSurfaceManager = null;
	
	// The input surface of the MediaCodec
	private SurfaceManager mCodecSurfaceManager = null;
	
	// Handles the rendering of the SurfaceTexture we got 
	// from the camera, onto a Surface
	private TextureManager mTextureManager = null;

	private Semaphore mLock = new Semaphore(0);

	// Allows to force the aspect ratio of the preview
	private ViewAspectRatioMeasurer mVARM = new ViewAspectRatioMeasurer();

     public SurfaceView()
     {

     }

    public SurfaceView(Context context, IAttributeSet attrs) {
		//base.con(context, attrs);

		mHandler = new Handler();

        this.Holder.AddCallback(this);
            //getHolder().addCallback(this);

            //mSyncObject

    }	

	public void setAspectRatioMode(int mode) {
		mAspectRatioMode = mode;
	}
	
	public SurfaceTexture getSurfaceTexture() {
		return mTextureManager.getSurfaceTexture();
	}

	public void addMediaCodecSurface(Surface surface) {
		lock (this) {
			mCodecSurfaceManager = new SurfaceManager(surface,mViewSurfaceManager);			
		}
	}

	public void removeMediaCodecSurface() {
		lock (this) {
			if (mCodecSurfaceManager != null) {
				mCodecSurfaceManager.release();
				mCodecSurfaceManager = null;
			}
		}
	}

	public void startGLThread() {
		Log.d(TAG,"Thread started.");
		if (mTextureManager == null) {
			mTextureManager = new TextureManager();
		}
		if (mTextureManager.getSurfaceTexture() == null) {
			mThread = new Thread(SurfaceView.this);
			mRunning = true;
			mThread.Start();
			mLock.AcquireUninterruptibly();
		}
	}
    /*
	@Override
	public void run() {

		mViewSurfaceManager = new SurfaceManager(getHolder().getSurface());
		mViewSurfaceManager.makeCurrent();
		mTextureManager.createTexture().setOnFrameAvailableListener(this);

		mLock.release();

		try {
			long ts = 0, oldts = 0;
			while (mRunning) {
				synchronized (mSyncObject) {
					mSyncObject.wait(2500);
					if (mFrameAvailable) {
						mFrameAvailable = false;

						mViewSurfaceManager.makeCurrent();
						mTextureManager.updateFrame();
						mTextureManager.drawFrame();
						mViewSurfaceManager.swapBuffer();

						if (mCodecSurfaceManager != null) {
							mCodecSurfaceManager.makeCurrent();
							mTextureManager.drawFrame();
							oldts = ts;
							ts = mTextureManager.getSurfaceTexture().getTimestamp();
							//Log.d(TAG,"FPS: "+(1000000000/(ts-oldts)));
							mCodecSurfaceManager.setPresentationTime(ts);
							mCodecSurfaceManager.swapBuffer();
						}

					} else {
						Log.e(TAG,"No frame received !");
					}
				}
			}
		} catch (InterruptedException ignore) {
		} finally {
			mViewSurfaceManager.release();
			mTextureManager.release();
		}
	} */

	public void OnFrameAvailable(SurfaceTexture surfaceTexture) {
		lock (this) {
			mFrameAvailable = true;
			this.NotifyAll();	
		}
	}
        
    public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Format format, int width,
			int height) {
	}

	
	public void SurfaceCreated(ISurfaceHolder holder) {
	}

	
	public void SurfaceDestroyed(ISurfaceHolder holder) {
		if (mThread != null) {
			mThread.Interrupt();
		}
		mRunning = false;
	}

	
    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec) {
		if (mVARM.getAspectRatio() > 0 && mAspectRatioMode == ASPECT_RATIO_PREVIEW) {
			mVARM.measure(widthMeasureSpec, heightMeasureSpec);
			SetMeasuredDimension(mVARM.getMeasuredWidth(), mVARM.getMeasuredHeight());
		} else {
			base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
		}
	}

	/**
	 * Requests a certain aspect ratio for the preview. You don't have to call this yourself, 
	 * the {@link VideoStream} will do it when it's needed.
	 */
	public void requestAspectRatio(double aspectRatio) {
            /*
		if (mVARM.getAspectRatio() != aspectRatio) {
			mVARM.setAspectRatio(aspectRatio);
			mHandler.post(new Runnable() {
				@Override
				public void run() {
					if (mAspectRatioMode == ASPECT_RATIO_PREVIEW) {
						requestLayout();
					}
				}
			});
		}
        */
	}

        /**
         * This class is a helper to measure views that require a specific aspect ratio.
         * @author Jesper Borgstrup
         */
        public class ViewAspectRatioMeasurer {

		private double aspectRatio;

		public void setAspectRatio(double aspectRatio) {
			this.aspectRatio = aspectRatio; 
		}

		public double getAspectRatio() {
			return this.aspectRatio; 
		}
		
		/**
		 * Measure with the aspect ratio given at construction.<br />
		 * <br />
		 * After measuring, get the width and height with the {@link #getMeasuredWidth()}
		 * and {@link #getMeasuredHeight()} methods, respectively.
		 * @param widthMeasureSpec The width <tt>MeasureSpec</tt> passed in your <tt>View.onMeasure()</tt> method
		 * @param heightMeasureSpec The height <tt>MeasureSpec</tt> passed in your <tt>View.onMeasure()</tt> method
		 */
		public void measure(int widthMeasureSpec, int heightMeasureSpec) {
			measure(widthMeasureSpec, heightMeasureSpec, this.aspectRatio);
		}

		/**
		 * Measure with a specific aspect ratio<br />
		 * <br />
		 * After measuring, get the width and height with the {@link #getMeasuredWidth()}
		 * and {@link #getMeasuredHeight()} methods, respectively.
		 * @param widthMeasureSpec The width <tt>MeasureSpec</tt> passed in your <tt>View.onMeasure()</tt> method
		 * @param heightMeasureSpec The height <tt>MeasureSpec</tt> passed in your <tt>View.onMeasure()</tt> method
		 * @param aspectRatio The aspect ratio to calculate measurements in respect to 
		 */
		public void measure(int widthMeasureSpec, int heightMeasureSpec, double aspectRatio) {
            MeasureSpecMode widthMode = MeasureSpec.GetMode(widthMeasureSpec);

			int widthSize = (widthMode == MeasureSpecMode.Unspecified) ? Integer.MaxValue : MeasureSpec.GetSize( widthMeasureSpec );
            MeasureSpecMode heightMode = MeasureSpec.GetMode( heightMeasureSpec );
			int heightSize = (heightMode == MeasureSpecMode.Unspecified) ? Integer.MaxValue : MeasureSpec.GetSize( heightMeasureSpec );

			if ( heightMode == MeasureSpecMode.Exactly && widthMode == MeasureSpecMode.Exactly) {
				/* 
				 * Possibility 1: Both width and height fixed
				 */
				measuredWidth =  widthSize;
				measuredHeight = heightSize;

			} else if ( heightMode == MeasureSpecMode.Exactly) {
				/*
				 * Possibility 2: Width dynamic, height fixed
				 */
				measuredWidth = (int)System.Math.Min( widthSize, heightSize * aspectRatio );
				measuredHeight = (int) (measuredWidth / aspectRatio);

			} else if ( widthMode == MeasureSpecMode.Exactly) {
				/*
				 * Possibility 3: Width fixed, height dynamic
				 */
				measuredHeight = (int)System.Math.Min( heightSize, widthSize / aspectRatio );
				measuredWidth = (int) (measuredHeight * aspectRatio);

			} else {
				/* 
				 * Possibility 4: Both width and height dynamic
				 */
				if ( widthSize > heightSize * aspectRatio ) {
					measuredHeight = heightSize;
					measuredWidth = (int)( measuredHeight * aspectRatio );
				} else {
					measuredWidth = widthSize;
					measuredHeight = (int) (measuredWidth / aspectRatio);
				}

			}
		}

		private int measuredWidth = 0;
		/**
		 * Get the width measured in the latest call to <tt>measure()</tt>.
		 */
		public int getMeasuredWidth() {
			/*if ( measuredWidth == null ) {
				throw new IllegalStateException( "You need to run measure() before trying to get measured dimensions" );
			}*/
			return measuredWidth;
		}

		private int measuredHeight = 0;
		/**
		 * Get the height measured in the latest call to <tt>measure()</tt>.
		 */
		public int getMeasuredHeight() {
			/*if ( measuredHeight == null ) {
				throw new IllegalStateException( "You need to run measure() before trying to get measured dimensions" );
			}*/
			return measuredHeight;
		}

	}

}
