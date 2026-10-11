export type ResourceKind = 'Room' | 'Activity' | 'Transportation';
export type BookingStatus = 'Pending' | 'Confirmed' | 'Cancelled' | 'Completed' | 'Expired' | 'Failed';
export interface ActivitySession { id: string; startUtc: string; endUtc: string; capacity: number; reservedSeats: number; price: number; version?: number; }
export interface CatalogItem {
  id: string; kind: ResourceKind; name: string; slug: string; description: string; tagline: string;
  imageUrl: string; galleryImages: string[]; basePrice: number; priceUnit: string; maxGuests: number; isActive: boolean;
  offerVersion: number; features: string[]; availableUnits: number; sessions: ActivitySession[];
}
export interface User { id: string; email: string; firstName: string; lastName: string; phoneNumber: string; role: string; }
export interface AuthResponse { accessToken: string; user: User; }
export interface Availability { available: boolean; availableCount: number; totalAmount: number; currency: string; offerVersion: number; cancellationHours: number; }
export interface Booking {
  id: string; bookingNumber: string; kind: ResourceKind; resourceId: string; resourceName: string; imageUrl: string;
  status: BookingStatus; startUtc: string; endUtc: string; guests: number; totalAmount: number; currency: string;
  expiresAtUtc?: string; createdAtUtc: string; cancellationDeadlineUtc: string; pickupLocation?: string;
  dropoffLocation?: string; notes?: string; priceLines: { description: string; quantity: number; unitPrice: number; total: number }[];
  assignedUnit?: string; version: number;
}
export interface Paged<T> { items: T[]; totalCount: number; page: number; pageSize: number; }
export interface Notice { id: string; subject: string; message: string; createdAtUtc: string; isRead: boolean; }
export interface Unit { id: string; name: string; isActive: boolean; }
export interface Audit { id: string; action: string; entityType: string; entityId: string; oldValue: string; newValue: string; adminEmail: string; createdAtUtc: string; correlationId: string; }
export interface Dashboard {
  totalBookings: number; confirmedBookings: number; cancelledBookings: number; completedBookings: number; pendingBookings: number;
  bookedValue: number; roomOccupancy: number; activityUtilization: number; transportationUtilization: number;
  daily: { date: string; bookings: number; value: number }[]; byKind: { kind: string; count: number; value: number }[]; upcoming: Booking[];
}
export interface SystemStatus { outboxPending: number; outboxFailed: number; notificationsPending: number; recentEvents: { id: string; type: string; createdAtUtc: string; status: string; attempts: number }[]; }
export interface Weather {
  location: string; temperature: number; apparentTemperature: number; windSpeed: number; weatherCode: number;
  isDay: boolean; condition: string; updatedAt: string;
  daily: { date: string; minimum: number; maximum: number; weatherCode: number; condition: string }[];
}
