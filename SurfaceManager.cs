/*
 * Based on the work of fadden
 * 
 * Copyright 2012 Google Inc. All Rights Reserved.
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
using Android.Opengl;
using Android.Views;
using Java.Lang;
using static Android.Resource;

namespace Net.Majorkernelpanic.Streaming
{


    public class SurfaceManager {

        public const System.String TAG = "TextureManager";

        private const int EGL_RECORDABLE_ANDROID = 0x3142;

        private EGLContext mEGLContext = null;
        private EGLContext mEGLSharedContext = null;
        private EGLSurface mEGLSurface = null;
        private EGLDisplay mEGLDisplay = null;

        private Surface mSurface;

        /**
         * Creates an EGL context and an EGL surface.
         */
        public SurfaceManager(Surface surface, SurfaceManager manager) {
            mSurface = surface;
            mEGLSharedContext = manager.mEGLContext;
            eglSetup();
        }

        /**
         * Creates an EGL context and an EGL surface.
         */
        public SurfaceManager(Surface surface) {
            mSurface = surface;
            eglSetup();
        }

        public void makeCurrent() {
            if (!EGL14.EglMakeCurrent(mEGLDisplay, mEGLSurface, mEGLSurface, mEGLContext))
                throw new Java.Lang.RuntimeException("eglMakeCurrent failed");
        }

        public void swapBuffer() {
            EGL14.EglSwapBuffers(mEGLDisplay, mEGLSurface);
        }

        /**
         * Sends the presentation time stamp to EGL.  Time is expressed in nanoseconds.
         */
        public void setPresentationTime(long nsecs) {
            EGLExt.EglPresentationTimeANDROID(mEGLDisplay, mEGLSurface, nsecs);
            checkEglError("eglPresentationTimeANDROID");
        }

        /**
         * Prepares EGL.  We want a GLES 2.0 context and a surface that supports recording.
         */
        private void eglSetup() {
            mEGLDisplay = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
            if (mEGLDisplay == EGL14.EglNoDisplay) {
                throw new Java.Lang.RuntimeException("unable to get EGL14 display");
            }
            int[] version = new int[2];
            if (!EGL14.EglInitialize(mEGLDisplay, version, 0, version, 1)) {
                throw new RuntimeException("unable to initialize EGL14");
            }

            // Configure EGL for recording and OpenGL ES 2.0.
            int[] attribList;
            if (mEGLSharedContext == null) {
                attribList = new int[] {
                    EGL14.EglRedSize, 8,
                    EGL14.EglGreenSize, 8,
                    EGL14.EglBlueSize, 8,
                    EGL14.EglRenderableType, EGL14.EglOpenglEs2Bit,
                    EGL14.EglNone
            };
            } else {
                attribList = new int[] {
                    EGL14.EglRedSize, 8,
                    EGL14.EglGreenSize, 8,
                    EGL14.EglBlueSize, 8,
                    EGL14.EglRenderableType, EGL14.EglOpenglEs2Bit,
                    EGL_RECORDABLE_ANDROID, 1,
                    EGL14.EglNone
            };
            }
            EGLConfig[] configs = new EGLConfig[1];
            int[] numConfigs = new int[1];
            EGL14.EglChooseConfig(mEGLDisplay, attribList, 0, configs, 0, configs.Length,
                    numConfigs, 0);
            checkEglError("eglCreateContext RGB888+recordable ES2");

            // Configure context for OpenGL ES 2.0.
            int[] attrib_list = {
                EGL14.EglContextClientVersion, 2,
                EGL14.EglNone
        };

            if (mEGLSharedContext == null) {
                mEGLContext = EGL14.EglCreateContext(mEGLDisplay, configs[0], EGL14.EglNoContext, attrib_list, 0);
            } else {
                mEGLContext = EGL14.EglCreateContext(mEGLDisplay, configs[0], mEGLSharedContext, attrib_list, 0);
            }
            checkEglError("eglCreateContext");

            // Create a window surface, and attach it to the Surface we received.
            int[] surfaceAttribs = {
                EGL14.EglNone
        };
            mEGLSurface = EGL14.EglCreateWindowSurface(mEGLDisplay, configs[0], mSurface,
                    surfaceAttribs, 0);
            checkEglError("eglCreateWindowSurface");

            GLES20.GlDisable(GLES20.GlDepthTest);
            GLES20.GlDisable(GLES20.GlCullFaceMode);

        }

        /**
         * Discards all resources held by this class, notably the EGL context.  Also releases the
         * Surface that was passed to our constructor.
         */
        public void release() {
            if (mEGLDisplay != EGL14.EglNoDisplay) {
                EGL14.EglMakeCurrent(mEGLDisplay, EGL14.EglNoSurface, EGL14.EglNoSurface,
                        EGL14.EglNoContext);
                EGL14.EglDestroySurface(mEGLDisplay, mEGLSurface);
                EGL14.EglDestroyContext(mEGLDisplay, mEGLContext);
                EGL14.EglReleaseThread();
                EGL14.EglTerminate(mEGLDisplay);
            }
            mEGLDisplay = EGL14.EglNoDisplay;
            mEGLContext = EGL14.EglNoContext;
            mEGLSurface = EGL14.EglNoSurface;
            mSurface.Release();
        }

        /**
         * Checks for EGL errors. Throws an exception if one is found.
         */
        private void checkEglError(System.String msg) {
            int error;
            if ((error = EGL14.EglGetError()) != EGL14.EglSuccess) {
                throw new RuntimeException(msg + ": EGL error: 0x" + Java.Lang.Integer.ToHexString(error));
            }
        }




    }
}