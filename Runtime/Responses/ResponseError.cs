namespace ApiClient.Runtime
{
    public class ResponseError<T> where T : System.Enum
    {
        public ResponseError(T errorCode)
        {
            ErrorCode = errorCode;
            NonDefaultErrorCodeAssigned = !errorCode.Equals(default(T));
        }

        public ResponseError(T errorCode, string userFacingErrorMessage)
        {
            ErrorCode = errorCode;
            UserFacingErrorMessage = userFacingErrorMessage;
            NonDefaultErrorCodeAssigned = !errorCode.Equals(default(T));
        }

        public T ErrorCode { get; private set; }
        public string UserFacingErrorMessage { get; private set; }
        public bool NonDefaultErrorCodeAssigned { get; private set; }

        public override string ToString()
        {
            return base.ToString();
        }
    }
}