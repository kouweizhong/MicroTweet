using System;
using Microsoft.SPOT;
using System.Collections;
using System.Net;
using System.Text;
using System.IO;

namespace MicroTweet
{
    /// <summary>
    /// MicroTweet Twitter client
    /// </summary>
    public class TwitterClient
    {
        /// <summary>
        /// Creates a TwitterClient with the specified application and user credentials.
        /// </summary>
        /// <param name="applicationCredentials">Application-specific credentials.</param>
        /// <param name="userCredentials">User-specific credentials.</param>
        public TwitterClient(OAuthApplicationCredentials applicationCredentials, OAuthUserCredentials userCredentials)
        {
            ApplicationCredentials = applicationCredentials;
            UserCredentials = userCredentials;
        }

        /// <summary>
        /// Gets or sets application-specific credentials.
        /// </summary>
        public OAuthApplicationCredentials ApplicationCredentials { get; set; }

        /// <summary>
        /// Gets or sets user-specific credentials.
        /// </summary>
        public OAuthUserCredentials UserCredentials { get; set; }

        /// <summary>
        /// Submits an authenticated HTTP request for Twitter's API.
        /// </summary>
        /// <param name="method">The HTTP method (e.g., "GET" or "POST").</param>
        /// <param name="uriBase">The base URI (without any query string parameters. The parameters will be added to the query string automatically for GET requests.</param>
        /// <param name="parameters">An IList of QueryParameters. These parameters will be included in the query string for GET requests, or the request body for POST requests.</param>
        protected TwitterResponse SubmitRequest(string method, string uriBase, IList parameters = null)
        {
            // If we have parameters, format them as a query string
            string parameterString = null;
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (i > 0)
                        parameterString += '&';
                    QueryParameter parameter = (QueryParameter)parameters[i];
                    parameterString += Utility.UriEncode(parameter.Key) + '=' + Utility.UriEncode(parameter.Value);
                }
            }

            // If this is a GET request, add the parameters to the URI
            string uri = uriBase;
            if (method == "GET" && parameterString != null)
                uri += '?' + parameterString;

            // Set up the HTTP request
            using (HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri))
            {
                request.Method = method;

                // Determine whether we need to submit data in the body of the request
                byte[] requestContentBytes = null;
                if (method == "POST" && parameterString != null)
                {
                    requestContentBytes = Encoding.UTF8.GetBytes(parameterString);
                    request.ContentLength = requestContentBytes.Length;
                    request.ContentType = "application/x-www-form-urlencoded";
                }

                // Add the OAuth authorization header to the request
                OAuth.SignHttpWebRequest(request, ApplicationCredentials, UserCredentials, parameters);

                // Begin submitting the request
                // If we need to submit content in the request body, get the request stream
                if (requestContentBytes != null)
                {
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(requestContentBytes, 0, requestContentBytes.Length);
                        requestStream.Flush();
                    }
                }

                // Get the response and response stream
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                {
                    // Set up the TwitterResponse object we'll return
                    TwitterResponse twitterResponse = new TwitterResponse();
                    twitterResponse.StatusCode = response.StatusCode;

                    // Set up a response buffer and read in the response
                    byte[] responseBytes = new byte[(int)response.ContentLength];

                    int index = 0;
                    int count = 100;
                    while (true)
                    {
                        index += responseStream.Read(responseBytes, index, count);
                        if (responseBytes.Length - index < count)
                            count = responseBytes.Length - index;
                        if (count <= 0)
                            break;
                    }

                    // Convert the response to a string
                    twitterResponse.ResponseBody = Encoding.UTF8.GetChars(responseBytes);

                    return twitterResponse;
                }
            }
        }

        /// <summary>
        /// Represents a response from Twitter's API.
        /// </summary>
        protected struct TwitterResponse
        {
            /// <summary>
            /// Gets the status code of the response.
            /// </summary>
            public HttpStatusCode StatusCode { get; set; }

            /// <summary>
            /// Gets the body of the response.
            /// </summary>
            public char[] ResponseBody { get; set; }
        }

        /// <summary>
        /// Verifies that the supplied user credentials are valid and returns the authenticating user's account information.
        /// </summary>
        public User GetCurrentUser()
        {
            var parameters = new QueryParameter[] { new QueryParameter("skip_status", "true") };

            var response = SubmitRequest("GET", "https://api.twitter.com/1.1/account/verify_credentials.json", parameters);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = Json.Parse(response.ResponseBody);
                return new User(this, (Hashtable)data);
            }

            throw new TwitterException(response.StatusCode, response.ResponseBody);
        }

        /// <summary>
        /// Posts a tweet to the authenticating user's timeline.
        /// </summary>
        /// <param name="message">The message content to post.</param>
        /// <returns>true if the tweet was posted successfully; otherwise, false.</returns>
        public Tweet SendTweet(string message)
        {
            var parameters = new QueryParameter[] { new QueryParameter("status", message) };

            var response = SubmitRequest("POST", "https://api.twitter.com/1.1/statuses/update.json", parameters);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = Json.Parse(response.ResponseBody);
                return new Tweet(this, (Hashtable)data);
            }

            throw new TwitterException(response.StatusCode, response.ResponseBody);
        }

        /// <summary>
        /// Returns a collection of the most recent tweets and retweets posted by the authenticating user and the users they follow.
        /// </summary>
        /// <param name="count">The maximum number of tweets to retrieve. Must be less than or equal to 200.</param>
        /// <param name="sinceID">If specified, returns results with an ID greater than (that is, more recent than) the specified ID.</param>
        /// <param name="maxID">If specified, returns results with an ID less than (that is, older than) or equal to the specified ID.</param>
        public IEnumerable GetHomeTimeline(int count = 5, long sinceID = -1, long maxID = -1)
        {
            var parameters = new ArrayList();
            parameters.Add(new QueryParameter("count", count.ToString()));
            if (sinceID >= 0)
                parameters.Add(new QueryParameter("since_id", sinceID.ToString()));
            if (maxID >= 0)
                parameters.Add(new QueryParameter("max_id", maxID.ToString()));

            var response = SubmitRequest("GET", "https://api.twitter.com/1.1/statuses/home_timeline.json", parameters);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var tweetList = Json.ParseArrayToObjects(response.ResponseBody, o => new Tweet(this, (Hashtable)o));
                return (Tweet[])tweetList.ToArray(typeof(Tweet));
            }

            throw new TwitterException(response.StatusCode, response.ResponseBody);
        }

        /// <summary>
        /// Returns account details for the specified user.
        /// </summary>
        /// <param name="screenName">The screen name of the user for whom to return results for (e.g., "twitter").</param>
        public User GetUser(string screenName)
        {
            return GetUser("screen_name", screenName);
        }

        /// <summary>
        /// Returns account details for the specified user.
        /// </summary>
        /// <param name="id">The ID of the user for whom to return results for.</param>
        public User GetUser(long id)
        {
            return GetUser("user_id", id.ToString());
        }

        private User GetUser(string parameterName, string parameterValue)
        {
            var parameters = new QueryParameter[] { new QueryParameter(parameterName, parameterValue) };

            var response = SubmitRequest("GET", "https://api.twitter.com/1.1/users/show.json", parameters);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = Json.Parse(response.ResponseBody);
                return new User(this, (Hashtable)data);
            }

            throw new TwitterException(response.StatusCode, response.ResponseBody);
        }

        /// <summary>
        /// Returns a collection of the most recent tweets and retweets posted by the specified user.
        /// </summary>
        /// <param name="screenName">The screen name of the user for whom to return results for (e.g., "twitter").</param>
        /// <param name="count">The maximum number of tweets to retrieve. Must be less than or equal to 200.</param>
        /// <param name="sinceID">If specified, returns results with an ID greater than (that is, more recent than) the specified ID.</param>
        /// <param name="maxID">If specified, returns results with an ID less than (that is, older than) or equal to the specified ID.</param>
        public IEnumerable GetUserTimeline(string screenName, int count = 5, long sinceID = -1, long maxID = -1)
        {
            return GetUserTimeline("screen_name", screenName, count, sinceID, maxID);
        }

        /// <summary>
        /// Returns a collection of the most recent tweets and retweets posted by the specified user.
        /// </summary>
        /// <param name="id">The ID of the user for whom to return results for.</param>
        /// <param name="count">The maximum number of tweets to retrieve. Must be less than or equal to 200.</param>
        /// <param name="sinceID">If specified, returns results with an ID greater than (that is, more recent than) the specified ID.</param>
        /// <param name="maxID">If specified, returns results with an ID less than (that is, older than) or equal to the specified ID.</param>
        public IEnumerable GetUserTimeline(long id, int count = 5, long sinceID = -1, long maxID = -1)
        {
            return GetUserTimeline("user_id", id.ToString(), count, sinceID, maxID);
        }

        private IEnumerable GetUserTimeline(string parameterName, string parameterValue, int count, long sinceID, long maxID)
        {
            var parameters = new ArrayList();
            parameters.Add(new QueryParameter(parameterName, parameterValue));
            parameters.Add(new QueryParameter("count", count.ToString()));
            if (sinceID >= 0)
                parameters.Add(new QueryParameter("since_id", sinceID.ToString()));
            if (maxID >= 0)
                parameters.Add(new QueryParameter("max_id", maxID.ToString()));

            var response = SubmitRequest("GET", "https://api.twitter.com/1.1/statuses/user_timeline.json", parameters);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var tweetList = Json.ParseArrayToObjects(response.ResponseBody, o => new Tweet(this, (Hashtable)o));
                return (Tweet[])tweetList.ToArray(typeof(Tweet));
            }

            throw new TwitterException(response.StatusCode, response.ResponseBody);
        }

        /// <summary>
        /// Returns a single tweet, specified by the id parameter.
        /// </summary>
        /// <param name="id">The numerical ID of the desired tweet.</param>
        public Tweet GetTweet(long id)
        {
            var parameters = new QueryParameter[] { new QueryParameter("id", id.ToString()) };

            var response = SubmitRequest("GET", "https://api.twitter.com/1.1/statuses/show.json", parameters);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = Json.Parse(response.ResponseBody);
                return new Tweet(this, (Hashtable)data);
            }

            throw new TwitterException(response.StatusCode, response.ResponseBody);
        }
    }
}
