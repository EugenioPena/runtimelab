// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Diagnostics;

namespace CoreLab
{


    internal static class Program
    {

        public static void testFunc()
        {

            // Console.WriteLine("test");
            // num = num * 2;
            // return num;

            try
            {
                try
                {
                    System.Convert.FromHexString(null);
                }
                catch (Exception)
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

        }

        public static void Main(string[] args)
        {
            testFunc();
            try
            {
                try
                {
                    System.Convert.FromHexString(null);
                }
                catch (Exception)
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
