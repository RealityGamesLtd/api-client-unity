namespace ApiClient.Runtime
{
    public class ResponseError<T> where T : System.Enum
    {
        public ResponseError(T errorCode)
        {
            ErrorCode = errorCode;
        }

        public ResponseError(T errorCode, string userFacingErrorMessage)
        {
            ErrorCode = errorCode;
            UserFacingErrorMessage = userFacingErrorMessage;
        }

        public T ErrorCode { get; private set; }
        public string UserFacingErrorMessage { get; private set; }

        public override string ToString()
        {
            return base.ToString();
        }
    }
}