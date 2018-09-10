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

using  Net.Majorkernelpanic.Streaming.Rtsp;
using Java.IO;
using Java.Net;
using Java.Util;
using Java.Util.Regex;
using Android.App;
using Android.Content;
using Android.OS;

using Android.Util;
using System;
using System.Collections.Generic;
using System.Threading;
using Java.Lang;
using Android.Preferences;


namespace Net.Majorkernelpanic.Streaming
{ 

    /**
     * Implementation of a subset of the RTSP protocol (RFC 2326).
     * 
     * It allows remote control of an android device cameras & microphone.
     * For each connected client, a Session is instantiated.
     * The Session will start or stop streams according to what the client wants.
     * 
     */
    public class RtspServer : Service , ISharedPreferencesOnSharedPreferenceChangeListener
    {

	    public const System.String TAG = "RtspServer";

	    /** The server name that will appear in responses. */
	    public const System.String SERVER_NAME = "MajorKernelPanic RTSP Server";

	    /** Port used by default. */
	    public const int DEFAULT_RTSP_PORT = 8086;

	    /** Port already in use. */
	    public const int ERROR_BIND_FAILED = 0x00;

	    /** A stream could not be started. */
	    public const int ERROR_START_FAILED = 0x01;

	    /** Streaming started. */
	    public const int MESSAGE_STREAMING_STARTED = 0X00;
	
	    /** Streaming stopped. */
	    public const int MESSAGE_STREAMING_STOPPED = 0X01;
	
	    /** Key used in the SharedPreferences to store whether the RTSP server is enabled or not. */
	    public const System.String KEY_ENABLED = "rtsp_enabled";

	    /** Key used in the SharedPreferences for the port used by the RTSP server. */
	    public const System.String KEY_PORT = "rtsp_port";

	    protected SessionBuilder mSessionBuilder;
	    protected ISharedPreferences mSharedPreferences;
	    protected bool mEnabled = true;	
	    protected int mPort = DEFAULT_RTSP_PORT;
	    protected WeakHashMap mSessions = new WeakHashMap(2);
	
	    private RequestListener mListenerThread;
	    private IBinder mBinder = new LocalBinder();
	    private bool mRestart = false;
	    private LinkedList<CallbackListener> mListeners = new LinkedList<CallbackListener>();

        /** Credentials for Basic Auth */
        private System.String mUsername;
        private System.String mPassword;

        private RtspServer selfObj;

        public RtspServer() {

            selfObj = this;

        }

	    /** Be careful: those callbacks won't necessarily be called from the ui thread ! */
	    public interface CallbackListener {

		    /** Called when an error occurs. */
		    void onError(RtspServer server, System.Exception e, int error);

		    /** Called when streaming starts/stops. */
		    void onMessage(RtspServer server, int message);
		
	    }

	    /**
	     * See {@link RtspServer.CallbackListener} to check out what events will be fired once you set up a listener.
	     * @param listener The listener
	     */
	    public void addCallbackListener(CallbackListener listener) {
		    lock (mListeners)
            {
			    if (mListeners.Count>0)
                {
				    foreach  (CallbackListener cl in mListeners)
                    {
					    if (cl == listener) return;
				    }
			    }
			    mListeners.AddLast(listener);			
		    }
	    }

	    /**
	     * Removes the listener.
	     * @param listener The listener
	     */
	    public void removeCallbackListener(CallbackListener listener) {
		    lock (mListeners)
            {
			    mListeners.Remove(listener);				
		    }
	    }

	    /** Returns the port used by the RTSP server. */	
	    public int getPort() {
		    return mPort;
	    }

	    /**
	     * Sets the port for the RTSP server to use.
	     * @param port The port
	     */
	    public void setPort(int port) {
		    ISharedPreferencesEditor editor = mSharedPreferences.Edit();
		    editor.PutString(KEY_PORT, port.ToString());
		    editor.Commit();
	    }

        /**
         * Set Basic authorization to access RTSP Stream
         * @param username username
         * @param password password
         */
        public void setAuthorization(System.String username, System.String password)
        {
            mUsername = username;
            mPassword = password;
        }

	    /** 
	     * Starts (or restart if needed, if for example the configuration 
	     * of the server has been modified) the RTSP server. 
	     */
	    public void start() {
		    if (!mEnabled || mRestart) stop();
		    if (mEnabled && mListenerThread == null) {
			    try {
				    mListenerThread = new RequestListener();
			    } catch (System.Exception e) {
				    mListenerThread = null;
			    }
		    }
		    mRestart = false;
	    }

	    /** 
	     * Stops the RTSP server but not the Android Service. 
	     * To stop the Android Service you need to call {@link android.content.Context#stopService(Intent)}; 
	     */
	    public void stop() {
		    if (mListenerThread != null) {
			    try {
				    mListenerThread.kill();
				    foreach ( Session session in mSessions.KeySet() ) {
				        if ( session != null && session.isStreaming() ) {
						    session.stop();
				        } 
				    }
			    } catch (System.Exception e) {
			    } finally {
				    mListenerThread = null;
			    }
		    }
	    }

	    /** Returns whether or not the RTSP server is streaming to some client(s). */
	    public bool isStreaming() {
		    foreach  ( Session session in mSessions.KeySet() ) {
		        if ( session != null && session.isStreaming() ) {
		    	    return true;
		        } 
		    }
		    return false;
	    }
	
	    public bool isEnabled() {
		    return mEnabled;
	    }

	    /** Returns the bandwidth consumed by the RTSP server in bits per second. */
	    public long getBitrate() {
		    long bitrate = 0;
		    foreach ( Session session in mSessions.KeySet() ) {
		        if ( session != null && session.isStreaming() ) {
		    	    bitrate += session.getBitrate();
		        } 
		    }
		    return bitrate;
	    }
	
	    public StartCommandResult onStartCommand(Intent intent, int flags, int startId) {
		    return StartCommandResult.Sticky;
	    }

	    public void onCreate() {

		    // Let's restore the state of the service 
		    mSharedPreferences = PreferenceManager.GetDefaultSharedPreferences(this);
		    mPort = Integer.ParseInt(mSharedPreferences.GetString(KEY_PORT,mPort.ToString()));
		    mEnabled = mSharedPreferences.GetBoolean(KEY_ENABLED, mEnabled);

		    // If the configuration is modified, the server will adjust
		    mSharedPreferences.RegisterOnSharedPreferenceChangeListener(this);

		    start();
	    }

	    
	    public void onDestroy() {
		    stop();
		    mSharedPreferences.UnregisterOnSharedPreferenceChangeListener(this);
	    }

		public void OnSharedPreferenceChanged(ISharedPreferences sharedPreferences, System.String key) {

			if (key.Equals(KEY_PORT)) {
				int port = Integer.ParseInt(sharedPreferences.GetString(KEY_PORT, mPort.ToString()));
				if (port != mPort) {
					mPort = port;
					mRestart = true;
					start();
				}
			}		
			else if (key.Equals(KEY_ENABLED)) {
				mEnabled = sharedPreferences.GetBoolean(KEY_ENABLED, mEnabled);
				start();
			}
		}

	    /** The Binder you obtain when a connection with the Service is established. */
	    public class LocalBinder : Binder {
		    public RtspServer getService()
            {
                //TODO
                return null;
               // return selfObj;// RtspServer.this;
		    }
	    }

	    public IBinder onBind(Intent intent) {
		    return mBinder;
	    }

	    protected void postMessage(int id) {
		    lock (mListeners) {
			    if (mListeners.Count!=0) {
				    foreach  (CallbackListener cl in mListeners) {
					    cl.onMessage(this, id);
				    }
			    }			
		    }
	    }	
	
	    protected void postError(System.Exception exception, int id) {
		    lock (mListeners) {
			    if (mListeners.Count!=0) {
				    foreach (CallbackListener cl in mListeners) {
					    cl.onError(this, exception, id);
				    }
			    }			
		    }
	    }

	    /** 
	     * By default the RTSP uses {@link UriParser} to parse the URI requested by the client
	     * but you can change that behavior by override this method.
	     * @param uri The uri that the client has requested
	     * @param client The socket associated to the client
	     * @return A proper session
	     */
	    protected Session handleRequest(System.String uri, Socket client)
        {
            //TODO
		    Session session = UriParser.parse(uri);
            session.setOrigin(client.LocalAddress.HostAddress);
		    if (session.getDestination()==null) {
			    session.setDestination(client.InetAddress.HostAddress);
		    }
		    return session;
	    }
	
	    class RequestListener : IRunnable {

		    private ServerSocket mServer;
            Java.Lang.Thread t;
            RtspServer parent;

            public IntPtr Handle => throw new NotImplementedException();

            private void Start()
            {
                t = new Java.Lang.Thread();
                t.Start();
            }
		    public RequestListener(RtspServer self ) {
			    try {
                    parent = self;
                    mServer = new ServerSocket(parent.getPort());
				    Start();
			    } catch (BindException e) {
				    Log.Error(TAG,"Port already in use !");
				    postError(e, ERROR_BIND_FAILED);
				    throw e;
			    }
		    }

		    public void Run() {
			    Log.Info(TAG,"RTSP server listening on port "+mServer.LocalPort);
			    while (!Java.Lang.Thread.Interrupted()) {
				    try {
					    new WorkerThread(mServer.Accept()).start();
				    } catch (SocketException e) {
					    break;
				    } catch (IOException e) {
					    Log.Error(TAG,e.Message);
					    continue;
				    }
			    }
			    Log.Info(TAG,"RTSP server stopped !");
		    }

		    public void kill() {
			    try {
				    mServer.Close();
			    } catch (IOException e) {}
			    try {
				    this.join();
			    } catch (InterruptedException ignore) {}
		    }

            public void Dispose()
            {
                //throw new NotImplementedException();
            }
        }

	    // One thread per client
	    class WorkerThread : IRunnable {

		    private Socket mClient;
		    private OutputStream mOutput;
		    private BufferedReader mInput;

		    // Each client has an associated session
		    private Session mSession;

		    public WorkerThread(Socket client) {
			    mInput = new BufferedReader(new InputStreamReader(client.InputStream));
			    mOutput = client.OutputStream;
			    mClient = client;
			    mSession = new Session();
		    }

		    public void Run() {
			    Request request;
			    Response response;

			    Log.i(TAG, "Connection from "+mClient.getInetAddress().getHostAddress());

			    while (!Thread.interrupted()) {

				    request = null;
				    response = null;

				    // Parse the request
				    try {
					    request = Request.parseRequest(mInput);
				    } catch (SocketException e) {
					    // Client has left
					    break;
				    } catch (Exception e) {
					    // We don't understand the request :/
					    response = new Response();
					    response.status = Response.STATUS_BAD_REQUEST;
				    }

				    // Do something accordingly like starting the streams, sending a session description
				    if (request != null) {
					    try {
						    response = processRequest(request);
					    }
					    catch (Exception e) {
						    // This alerts the main thread that something has gone wrong in this thread
						    postError(e, ERROR_START_FAILED);
						    Log.e(TAG,e.getMessage()!=null?e.getMessage():"An error occurred");
						    e.printStackTrace();
						    response = new Response(request);
					    }
				    }

				    // We always send a response
				    // The client will receive an "INTERNAL SERVER ERROR" if an exception has been thrown at some point
				    try {
					    response.send((mOutput);
				    } catch (IOException e) {
					    Log.e(TAG,"Response was not sent properly");
					    break;
				    }

			    }

			    // Streaming stops when client disconnects
			    boolean streaming = isStreaming();
			    mSession.syncStop();
			    if (streaming && !isStreaming()) {
				    postMessage(MESSAGE_STREAMING_STOPPED);
			    }
			    mSession.release();

			    try {
				    mClient.close();
			    } catch (IOException ignore) {}

			    Log.i(TAG, "Client disconnected");

		    }

		    public Response processRequest(Request request) public void Dispose()
            {
                throw new NotImplementedException();
            }

            throws IllegalStateException, IOException {
			    Response response = new Response(request);

                //Ask for authorization unless this is an OPTIONS request
                ifpublic IntPtr Handle => throw new NotImplementedException();

            (!isAuthorized(request) && !request.method.equalsIgnoreCase("OPTIONS"))
                {
                    response.attributes = "WWW-Authenticate: Basic realm=\""+SERVER_NAME+"\"\r\n";
                    response.status = Response.STATUS_UNAUTHORIZED;
                }
                else
                {
			        /* ********************************************************************************** */
			        /* ********************************* Method DESCRIBE ******************************** */
			        /* ********************************************************************************** */
                    if (request.method.equalsIgnoreCase("DESCRIBE")) {

                        // Parse the requested URI and configure the session
                        mSession = handleRequest(request.uri, mClient);
                        mSessions.put(mSession, null);
                        mSession.syncConfigure();

                        String requestContent = mSession.getSessionDescription();
                        String requestAttributes =
                                "Content-Base: " + mClient.getLocalAddress().getHostAddress() + ":" + mClient.getLocalPort() + "/\r\n" +
                                        "Content-Type: application/sdp\r\n";

                        response.attributes = requestAttributes;
                        response.content = requestContent;

                        // If no exception has been thrown, we reply with OK
                        response.status = Response.STATUS_OK;

                    }

                    /* ********************************************************************************** */
                    /* ********************************* Method OPTIONS ********************************* */
                    /* ********************************************************************************** */
                    else if (request.method.equalsIgnoreCase("OPTIONS")) {
                        response.status = Response.STATUS_OK;
                        response.attributes = "Public: DESCRIBE,SETUP,TEARDOWN,PLAY,PAUSE\r\n";
                        response.status = Response.STATUS_OK;
                    }

                    /* ********************************************************************************** */
                    /* ********************************** Method SETUP ********************************** */
                    /* ********************************************************************************** */
                    else if (request.method.equalsIgnoreCase("SETUP")) {
                        Pattern p;
                        Matcher m;
                        int p2, p1, ssrc, trackId, src[];
                        String destination;

                        p = Pattern.compile("trackID=(\\w+)", Pattern.CASE_INSENSITIVE);
                        m = p.matcher(request.uri);

                        if (!m.find()) {
                            response.status = Response.STATUS_BAD_REQUEST;
                            return response;
                        }

                        trackId = Integer.parseInt(m.group(1));

                        if (!mSession.trackExists(trackId)) {
                            response.status = Response.STATUS_NOT_FOUND;
                            return response;
                        }

                        p = Pattern.compile("client_port=(\\d+)-(\\d+)", Pattern.CASE_INSENSITIVE);
                        m = p.matcher(request.headers.get("transport"));

                        if (!m.find()) {
                            int[] ports = mSession.getTrack(trackId).getDestinationPorts();
                            p1 = ports[0];
                            p2 = ports[1];
                        } else {
                            p1 = Integer.parseInt(m.group(1));
                            p2 = Integer.parseInt(m.group(2));
                        }

                        ssrc = mSession.getTrack(trackId).getSSRC();
                        src = mSession.getTrack(trackId).getLocalPorts();
                        destination = mSession.getDestination();

                        mSession.getTrack(trackId).setDestinationPorts(p1, p2);

                        boolean streaming = isStreaming();
                        mSession.syncStart(trackId);
                        if (!streaming && isStreaming()) {
                            postMessage(MESSAGE_STREAMING_STARTED);
                        }

                        response.attributes = "Transport: RTP/AVP/UDP;" + (InetAddress.getByName(destination).isMulticastAddress() ? "multicast" : "unicast") +
                                ";destination=" + mSession.getDestination() +
                                ";client_port=" + p1 + "-" + p2 +
                                ";server_port=" + src[0] + "-" + src[1] +
                                ";ssrc=" + Integer.toHexString(ssrc) +
                                ";mode=play\r\n" +
                                "Session: " + "1185d20035702ca" + "\r\n" +
                                "Cache-Control: no-cache\r\n";
                        response.status = Response.STATUS_OK;

                        // If no exception has been thrown, we reply with OK
                        response.status = Response.STATUS_OK;

                    }

                    /* ********************************************************************************** */
                    /* ********************************** Method PLAY *********************************** */
                    /* ********************************************************************************** */
                    else if (request.method.equalsIgnoreCase("PLAY")) {
                        String requestAttributes = "RTP-Info: ";
                        if (mSession.trackExists(0))
                            requestAttributes += "url=rtsp://" + mClient.getLocalAddress().getHostAddress() + ":" + mClient.getLocalPort() + "/trackID=" + 0 + ";seq=0,";
                        if (mSession.trackExists(1))
                            requestAttributes += "url=rtsp://" + mClient.getLocalAddress().getHostAddress() + ":" + mClient.getLocalPort() + "/trackID=" + 1 + ";seq=0,";
                        requestAttributes = requestAttributes.substring(0, requestAttributes.length() - 1) + "\r\nSession: 1185d20035702ca\r\n";

                        response.attributes = requestAttributes;

                        // If no exception has been thrown, we reply with OK
                        response.status = Response.STATUS_OK;

                    }

                    /* ********************************************************************************** */
                    /* ********************************** Method PAUSE ********************************** */
                    /* ********************************************************************************** */
                    else if (request.method.equalsIgnoreCase("PAUSE")) {
                        response.status = Response.STATUS_OK;
                    }

                    /* ********************************************************************************** */
                    /* ********************************* Method TEARDOWN ******************************** */
                    /* ********************************************************************************** */
                    else if (request.method.equalsIgnoreCase("TEARDOWN")) {
                        response.status = Response.STATUS_OK;
                    }

                    /* ********************************************************************************** */
                    /* ********************************* Unknown method ? ******************************* */
                    /* ********************************************************************************** */
                    else {
                        Log.e(TAG, "Command unknown: " + request);
                        response.status = Response.STATUS_BAD_REQUEST;
                    }
                }
			    return response;

		    }

            /**
             * Check if the request is authorized
             * @param request
             * @return true or false
             */
            private boolean isAuthorized(Request request)
            {
                String auth = request.headers.get("authorization");
                if(mUsername == null || mPassword == null || mUsername.isEmpty())
                    return true;

                if(auth != null && !auth.isEmpty())
                {
                    String received = auth.substring(auth.lastIndexOf(" ")+1);
                    String local = mUsername+":"+mPassword;
                    String localEncoded = Base64.encodeToString(local.getBytes(),Base64.NO_WRAP);
                    if(localEncoded.equals(received))
                        return true;
                }

                return false;
            }
	    }

	    static class Request {

		    // Parse method & uri
		    public static final Pattern regexMethod = Pattern.compile("(\\w+) (\\S+) RTSP",Pattern.CASE_INSENSITIVE);
		    // Parse a request header
		    public static final Pattern rexegHeader = Pattern.compile("(\\S+):(.+)",Pattern.CASE_INSENSITIVE);

		    public String method;
		    public String uri;
		    public HashMap<String,String> headers = new HashMap<String,String>();

		    /** Parse the method, uri & headers of a RTSP request */
		    public static Request parseRequest(BufferedReader input) throws IOException, IllegalStateException, SocketException {
			    Request request = new Request();
			    String line;
			    Matcher matcher;

			    // Parsing request method & uri
			    if ((line = input.readLine())==null) throw new SocketException("Client disconnected");
			    matcher = regexMethod.matcher(line);
			    matcher.find();
			    request.method = matcher.group(1);
			    request.uri = matcher.group(2);

			    // Parsing headers of the request
			    while ( (line = input.readLine()) != null && line.length()>3 ) {
				    matcher = rexegHeader.matcher(line);
				    matcher.find();
				    request.headers.put(matcher.group(1).toLowerCase(Locale.US),matcher.group(2));
			    }
			    if (line==null) throw new SocketException("Client disconnected");

			    // It's not an error, it's just easier to follow what's happening in logcat with the request in red
			    Log.e(TAG,request.method+" "+request.uri);

			    return request;
		    }
	    }
        /*
	    static class Response {

		    // Status code definitions
		    public static final String STATUS_OK = "200 OK";
		    public static final String STATUS_BAD_REQUEST = "400 Bad Request";
            public static final String STATUS_UNAUTHORIZED = "401 Unauthorized";
		    public static final String STATUS_NOT_FOUND = "404 Not Found";
		    public static final String STATUS_INTERNAL_SERVER_ERROR = "500 Internal Server Error";

		    public String status = STATUS_INTERNAL_SERVER_ERROR;
		    public String content = "";
		    public String attributes = "";

		    private final Request mRequest;

		    public Response(Request request) {
			    this.mRequest = request;
		    }

		    public Response() {
			    // Be carefull if you modify the send() method because request might be null !
			    mRequest = null;
		    }

		    public void send(OutputStream output) throws IOException {
			    int seqid = -1;

			    try {
				    seqid = Integer.parseInt(mRequest.headers.get("cseq").replace(" ",""));
			    } catch (Exception e) {
				    Log.e(TAG,"Error parsing CSeq: "+(e.getMessage()!=null?e.getMessage():""));
			    }

			    String response = 	"RTSP/1.0 "+status+"\r\n" +
					    "Server: "+SERVER_NAME+"\r\n" +
					    (seqid>=0?("Cseq: " + seqid + "\r\n"):"") +
					    "Content-Length: " + content.length() + "\r\n" +
					    attributes +
					    "\r\n" + 
					    content;

			    Log.d(TAG,response.replace("\r", ""));

			    output.write(response.getBytes());
		    }
	    }
        */
    }
}
