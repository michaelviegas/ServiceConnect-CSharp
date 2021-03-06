﻿using System;
using PublishSubscribe.Messages;
using ServiceConnect;

namespace PublishSubscribe.Publisher
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("*********** Producer ***********");
            var bus = Bus.Initialize(config =>
            {
            });

            while (true)
            {
                Console.WriteLine("Press enter to publish message");
                Console.ReadLine();

                for (int i = 0; i < 1000000; i++)
                {
                    var id = Guid.NewGuid();
                    bus.Publish(new PublishSubscribeMessage(id));
                }

                bus.Dispose();
            }
        }
    }
}
