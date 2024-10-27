namespace Wacs.WASIp1.Types
{
    public enum Advice : byte
    {
        /// <summary>
        /// The application has no advice to give on its behavior with respect to the specified data.
        /// </summary>
        Normal     = 0, 

        /// <summary>
        /// The application expects to access the specified data sequentially from lower offsets to higher offsets.
        /// </summary>
        Sequential = 1, 

        /// <summary>
        /// The application expects to access the specified data in a random order.
        /// </summary>
        Random     = 2, 

        /// <summary>
        /// The application expects to access the specified data in the near future.
        /// </summary>
        WillNeed   = 3, 

        /// <summary>
        /// The application expects that it will not access the specified data in the near future.
        /// </summary>
        DontNeed   = 4, 

        /// <summary>
        /// The application expects to access the specified data once and then not reuse it thereafter.
        /// </summary>
        NoReuse    = 5  
    }
}